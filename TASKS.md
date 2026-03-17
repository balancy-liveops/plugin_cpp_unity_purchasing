# Balancy Purchasing — Bug Fixes & Improvement Tasks

This document describes all known issues in the BalancyPayments module (v1.0.0) with exact file locations, root cause analysis, and fix instructions.

**Source files:**
- `IBalancyPaymentSystem.cs` — Interface and data types
- `BalancyPaymentManager.cs` — Main manager (singleton MonoBehaviour)
- `UnityPurchaseSystem.cs` — Unity IAP v5 implementation
- `PendingPurchaseManager.cs` — Pending purchase persistence

---

## TASK 1: `CleanupOldPendingPurchases` destroys valid `WaitingForStore` entries on every app startup

**Severity:** Critical — causes real money purchases to be lost
**File:** `PendingPurchaseManager.cs`, line 248

### Problem

The cleanup condition uses `||` which removes ALL `WaitingForStore` entries regardless of age:

```csharp
// Line 248
int removedCount = _data.Purchases.RemoveAll(p => p.Timestamp < cutoffTime || p.Status == PendingStatus.WaitingForStore);
```

`CleanupOldPendingPurchases()` is called from `LoadPendingPurchases()` (line 275), which runs in the `PendingPurchaseManager` constructor (line 81), which runs the first time `PendingPurchaseManager.Instance` is accessed — i.e., on every app startup.

This means: if a purchase was initiated (status = `WaitingForStore`), the app crashed before the store responded, and then the app restarts — the pending record is deleted before the store can match it. When `OnPurchasesFetched` later calls `ProcessPendingOrder`, it looks for a `WaitingForStore` entry (`UnityPurchaseSystem.cs` line 801), finds nothing, logs an error, and **silently drops the purchase**. The user paid but never gets their item.

### Fix

Change the condition on line 248 to only remove old entries, not all `WaitingForStore` entries:

```csharp
int removedCount = _data.Purchases.RemoveAll(p => p.Timestamp < cutoffTime);
```

This still cleans up stale entries older than 30 days (including stale `WaitingForStore` ones) but preserves recent `WaitingForStore` entries that the store might still deliver.

---

## TASK 2: `ProcessPendingOrder` fails when no matching `WaitingForStore` record exists (e.g., during restore)

**Severity:** High — restore flow silently drops purchases
**File:** `UnityPurchaseSystem.cs`, lines 800–808

### Problem

`ProcessPendingOrder` requires a matching pending purchase with status `WaitingForStore`:

```csharp
// Line 801
var pendingPurchase = _pendingPurchaseManager.GetPendingPurchaseByProductId(productId, PendingStatus.WaitingForStore);

// Line 804-808
if (pendingPurchase == null)
{
    Debug.LogError($"Failed to find pending purchase for product: {productId}");
    return;  // <— silently drops the order
}
```

This fails in two scenarios:
1. **Restore flow**: `OnPurchasesFetched` during init calls `ProcessPendingOrder` for orders returned by the store (line 737-740). Restored purchases from a previous install have no local `PendingPurchase` record — they were never initiated on this device.
2. **After TASK 1 fix is incomplete**: Even with the cleanup fix, if the store returns a purchase from a previous session that was already cleaned up, it will still fail.

### Fix

When no `WaitingForStore` record is found, create one on the fly instead of returning. The order has all the data needed. You need to construct a `BalancyProductInfo` from the product ID (you can look up the product in the store to determine the type). Something like:

```csharp
if (pendingPurchase == null)
{
    // This is likely a restored purchase or one from a previous session.
    // Create a pending record so validation can proceed.
    Debug.Log($"No pending purchase found for {productId}, creating one (likely a restore or previous session).");

    // You'll need to construct a BalancyProductInfo here.
    // At minimum, use the product ID and look up the product type from _productDefinitions.
    var balancyProductInfo = new Balancy.Actions.BalancyProductInfo(/* appropriate constructor */);
    pendingPurchase = _pendingPurchaseManager.AddPendingPurchase(balancyProductInfo);
}
```

The exact constructor for `BalancyProductInfo` depends on the Balancy SDK API. Alternatively, you could add a fallback constructor that takes just a product ID string.

**Important**: The restore path (lines 824-846) also depends on this method succeeding to add entries to `_restoredPurchases`. If `ProcessPendingOrder` returns early, the restore callback will report 0 restored purchases even though the store returned them.

---

## TASK 3: Duplicate purchase guard is disabled — rapid taps can create duplicate pending entries

**Severity:** Medium — causes duplicate charges and duplicate validation attempts
**Files:**
- `PendingPurchaseManager.cs`, lines 91–99 (commented out)
- `BalancyPaymentManager.cs`, lines 251–266 (commented out)

### Problem

Both guard blocks that prevent duplicate purchases for the same product are commented out:

**PendingPurchaseManager.cs lines 91-99:**
```csharp
// Check if this product is already pending
// var existing = _data.Purchases.Find(p => p.Equals(productInfo) &&
//     (p.Status == PendingStatus.WaitingForStore || p.Status == PendingStatus.ProcessingValidation));
//
// if (existing != null)
// {
//     Debug.LogWarning($"Product {productInfo.ProductId} already has a pending purchase. Returning existing.");
//     return existing;
// }
```

**BalancyPaymentManager.cs lines 251-266:**
```csharp
// var pendingPurchase = _pendingPurchaseManager.GetPendingPurchaseByProductId(productId);
// if (pendingPurchase != null &&
//     (pendingPurchase.Status == PendingStatus.WaitingForStore ||
//      pendingPurchase.Status == PendingStatus.ProcessingValidation))
// {
//     LogWarning($"Purchase already in progress for product: {productId}");
//     _paymentSystem.ReportPaymentStatusToBalancy(productInfo, new PurchaseResult
//     {
//         Status = PurchaseStatus.Pending,
//         ProductId = productId,
//         ErrorMessage = "Purchase already in progress"
//     });
//     return;
// }
```

Without these guards, if the user taps a purchase button twice quickly:
- Two `PendingPurchase` records are created for the same product
- `GetPendingPurchaseByProductId` (used in `ProcessPendingOrder` and `OnPurchaseFailed`) returns the **first** match, so the second record may never be cleaned up
- This can cause orphaned pending entries that accumulate on disk

### Fix

Uncomment both guard blocks. The `PendingPurchaseManager` guard (lines 91-99) is the primary defense. The `BalancyPaymentManager` guard (lines 251-266) is a secondary check.

For the `PendingPurchaseManager.AddPendingPurchase` method, uncomment lines 92-99 so it returns the existing entry instead of creating a duplicate.

For `BalancyPaymentManager.PurchaseProduct`, uncomment lines 252-266 so it reports `Pending` status to Balancy instead of initiating a second purchase flow.

---

## TASK 4: `RefreshProductList` is a no-op — clears cache but never repopulates it

**Severity:** Medium — `GetProducts`/`GetProduct` return empty lists when cache is empty
**File:** `UnityPurchaseSystem.cs`, lines 574–585

### Problem

```csharp
private void RefreshProductList()
{
    _cachedProducts.Clear();

    if (_storeController == null)
    {
        return;
    }

    // In v5, products are accessed through events
    // This method will be called from OnProductsFetched event handler
}
```

The method clears `_cachedProducts` and does nothing else. It's called from:
- `GetProducts` (line 227) — clears cache, then line 228 returns the now-empty `_cachedProducts`
- `GetProduct` (line 250) — clears cache, then line 251 searches the now-empty list

The comment says "This method will be called from OnProductsFetched event handler" but that's not what happens — `OnProductsFetched` populates `_cachedProducts` directly (lines 680-723), it doesn't call `RefreshProductList`.

### Fix

Option A (recommended): Remove `RefreshProductList` entirely and change `GetProducts`/`GetProduct` to call `_storeController.FetchProducts()` which will trigger the `OnProductsFetched` event and repopulate the cache. The callback would need to be deferred until `OnProductsFetched` fires.

Option B (simpler): Make `RefreshProductList` call `_storeController.FetchProducts(CreateProductDefinitions())` to trigger a real refresh. But you'd also need to make `GetProducts`/`GetProduct` wait for the `OnProductsFetched` callback rather than reading the cache synchronously after calling refresh.

Option C (simplest, works now): Remove the `RefreshProductList()` calls from `GetProducts` (line 227) and `GetProduct` (line 250). If the cache is empty and the system is initialized, products should already have been fetched during initialization. Simply return the empty list/null without clearing the cache again:

```csharp
// In GetProducts — replace lines 227-228:
callback?.Invoke(_cachedProducts);

// In GetProduct — replace lines 250-252:
callback?.Invoke(null);
```

---

## TASK 5: `GetSubscriptionsInfo` returns hardcoded placeholder data

**Severity:** Medium — subscription status is always wrong
**File:** `UnityPurchaseSystem.cs`, lines 384–430

### Problem

The method iterates over subscription product definitions and returns hardcoded dummy data:

```csharp
var subInfo = new SubscriptionInfo
{
    ProductId = productDef.id,
    PurchaseDate = DateTime.Now,              // WRONG: always "now"
    ExpireDate = DateTime.Now.AddMonths(1),   // WRONG: always 1 month from now
    IsSubscribed = false,                     // WRONG: always false
    IsExpired = false,                        // WRONG: always false
    IsCancelled = false,                      // WRONG: always false
    IsFreeTrial = false,                      // WRONG: always false
    IsAutoRenewing = true,                    // WRONG: always true
    RemainingTime = TimeSpan.FromDays(30),    // WRONG: always 30 days
    // ...
};
```

Every field is a placeholder. This returns misleading data to the caller.

### Fix

Option A (proper implementation): Use the Unity IAP v5 API to query real subscription status. After `OnProductsFetched`, the `Product` objects in `_cachedProducts` contain actual purchase and subscription data. Use `product.RawProductData` (which is the original Unity IAP `Product`) to access `product.receipt` and parse subscription info using Unity's `SubscriptionManager` or the v5 equivalent.

Option B (honest stub): If real subscription data isn't available yet via IAP v5, log a warning and return an empty list instead of fake data. This is better than returning incorrect data that callers may act on:

```csharp
public void GetSubscriptionsInfo(Action<List<SubscriptionInfo>> callback)
{
    Debug.LogWarning("GetSubscriptionsInfo is not yet implemented for IAP v5. Returning empty list.");
    callback?.Invoke(new List<SubscriptionInfo>());
}
```

---

## TASK 6: `LogError` is suppressed when `debugMode` is false

**Severity:** Low-Medium — errors are silently swallowed in production
**File:** `BalancyPaymentManager.cs`, lines 484–490

### Problem

```csharp
private void LogError(string message)
{
    if (debugMode)
    {
        Debug.LogError($"[BalancyPayments] {message}");
    }
}
```

The `debugMode` field (line 51) gates all logging including errors. When `debugMode` is `false` (as it should be in production to reduce log noise), error messages like "Payment system initialization failed" are silently swallowed. This makes production issues nearly impossible to diagnose.

Similarly, `LogWarning` (lines 476-482) is also gated.

### Fix

Always log errors and warnings regardless of `debugMode`. Only gate `Log` (info-level) behind the flag:

```csharp
private void Log(string message)
{
    if (debugMode)
    {
        Debug.Log($"[BalancyPayments] {message}");
    }
}

private void LogWarning(string message)
{
    Debug.LogWarning($"[BalancyPayments] {message}");
}

private void LogError(string message)
{
    Debug.LogError($"[BalancyPayments] {message}");
}
```

---

## TASK 7: Race condition — `ConfirmPurchase` is called before server validation completes

**Severity:** Medium — could confirm purchases that fail server validation
**File:** `UnityPurchaseSystem.cs`, lines 848–851

### Problem

In `ProcessPendingOrder`, after updating the pending purchase, the code calls both validation and confirmation:

```csharp
// Line 848
ValidatePurchaseReceipt(pendingPurchase);

// Line 851
_storeController.ConfirmPurchase(order);
```

`ValidatePurchaseReceipt` (line 483) calls `ReportPaymentStatusToBalancy` (line 508), which calls `Balancy.API.FinalizedHardPurchase` (line 528) — this is an **async** call with a callback. But `ConfirmPurchase` is called immediately after, synchronously.

This means the purchase is confirmed with the store (Apple/Google) before the Balancy server has validated the receipt. If server validation fails (e.g., receipt is invalid, fraudulent), the purchase has already been confirmed and cannot be revoked at the store level.

### Fix

Move `_storeController.ConfirmPurchase(order)` inside the `FinalizedHardPurchase` success callback. This requires passing the `order` reference through to `ReportPaymentStatusToBalancy`.

One approach: add an optional `Action` parameter to `ValidatePurchaseReceipt` and `ReportPaymentStatusToBalancy` for a "on validation success" callback:

```csharp
private void ProcessPendingOrder(PendingOrder order)
{
    // ... existing code up to line 847 ...

    ValidatePurchaseReceipt(pendingPurchase, onValidationSuccess: () =>
    {
        _storeController.ConfirmPurchase(order);
    });

    // Remove the line: _storeController.ConfirmPurchase(order);
}
```

Then in `ReportPaymentStatusToBalancy`, call `onValidationSuccess` inside the `validationSuccess == true` branch (around line 530).

**Note:** This also addresses the `//TODO report to apple for claiming` comment on line 536 — confirming the purchase with the store IS the claiming step.

---

## TASK 8: Dead code — `OnAppleRestoreTransactionsComplete` and `OnGooglePlayRestoreTransactionsComplete` are never called

**Severity:** Low — dead code, no functional impact
**File:** `UnityPurchaseSystem.cs`, lines 610–623

### Problem

Two methods exist for platform-specific restore completion:

```csharp
private void OnAppleRestoreTransactionsComplete(bool success, string errorMessage) { ... }
private void OnGooglePlayRestoreTransactionsComplete(bool success, string errorMessage) { ... }
```

Neither is referenced anywhere in the codebase. The actual restore completion is handled by the lambda in `RestorePurchases` (line 369):

```csharp
_storeController.RestoreTransactions((success, error) => {
    OnRestoreTransactionsComplete(success);
});
```

### Fix

Delete both methods (`OnAppleRestoreTransactionsComplete` and `OnGooglePlayRestoreTransactionsComplete`). They serve no purpose.

Also delete `GetAppStore()` (lines 590-605) — it is also never called anywhere.

---

## TASK 9: Dead code — `_validationQueue` in `BalancyPaymentManager` is never used

**Severity:** Low — dead code
**File:** `BalancyPaymentManager.cs`, lines 66–73

### Problem

```csharp
// Track purchases that are waiting for validation
private Dictionary<string, PurchasePendingValidation> _validationQueue = new Dictionary<string, PurchasePendingValidation>();

private class PurchasePendingValidation
{
    public PurchaseReceipt Receipt;
    public Action<PurchaseResult> Callback;
}
```

`_validationQueue` and `PurchasePendingValidation` are declared but never read or written anywhere.

### Fix

Delete lines 66-73. Remove the unused field and class.

---

## TASK 10: `BalancyPaymentManager.Instance` is `private` — public methods are inaccessible

**Severity:** Medium — API is unusable from game code
**File:** `BalancyPaymentManager.cs`, line 25

### Problem

```csharp
private static BalancyPaymentManager Instance
```

The singleton accessor is `private`. The class exposes public instance methods like `GetProducts`, `GetProduct`, `RestorePurchases`, `GetSubscriptionsInfo`, `IsInitialized`, and `IsPurchasingSupported` — but external code cannot call them because `Instance` is private.

The only way to use these methods is through the Balancy callback system (which calls internal methods). Direct usage like `BalancyPaymentManager.Instance.GetProducts(...)` is impossible from game code.

### Fix

The intended developer-facing API is through `Balancy.API` (e.g., `Balancy.API.InitPurchase()`, `Balancy.API.RestorePurchases()`) and `Balancy.Callbacks` — not through `BalancyPaymentManager` directly. The `Instance` being private is by design.

Make the public methods on `BalancyPaymentManager` `internal` instead, since they are only called by the Balancy SDK internals:
- `GetProducts` → `internal`
- `GetProduct` → `internal`
- `RestorePurchases` → `internal`
- `GetSubscriptionsInfo` → `internal`
- `IsInitialized` → `internal`
- `IsPurchasingSupported` → `internal`

---

## TASK 11: `ApplyConfig` can throw `IndexOutOfRangeException` if `GetProductsIdAndType` returns odd-length array

**Severity:** Low — defensive coding
**File:** `BalancyPaymentManager.cs`, lines 165–176

### Problem

```csharp
for (int i = 0; i < productsAndTypes.Length; i += 2)
{
    var id = productsAndTypes[i];
    if (int.TryParse(productsAndTypes[i + 1], out var type))  // <— i+1 could be out of bounds
```

If `Balancy.API.GetProductsIdAndType()` returns an array with an odd number of elements, `productsAndTypes[i + 1]` on the last iteration will throw `IndexOutOfRangeException`.

### Fix

Add a bounds check:

```csharp
for (int i = 0; i + 1 < productsAndTypes.Length; i += 2)
```

---

## TASK 12: `debugMode` field uses `[SerializeField]` but the GameObject has `HideFlags.HideAndDontSave`

**Severity:** Low — `debugMode` can never be changed in the Inspector
**File:** `BalancyPaymentManager.cs`, lines 31, 51

### Problem

The `BalancyPaymentManager` GameObject is created with `HideFlags.HideAndDontSave` (line 32), which hides it from the Inspector. But `debugMode` is marked `[SerializeField]` (line 51) — a serialized field that's only useful when visible in the Inspector.

Since the GameObject is hidden, there's no way to toggle `debugMode` through the Unity Editor.

### Fix

Either:
- Make `debugMode` a `public static bool` that can be set from code, or
- Add a public static method `SetDebugMode(bool enabled)`, or
- Remove `[SerializeField]` and make it a constant or a static property

---

## TASK 13: Commented-out restore validation code in `BalancyPaymentManager.RestorePurchases`

**Severity:** Low — dead commented code, but indicates incomplete feature
**File:** `BalancyPaymentManager.cs`, lines 304–323

### Problem

A large block of commented-out code references `PurchaseStatus.Restored` and `PurchaseStatus.Validated`/`PurchaseStatus.Invalid` which don't exist in the `PurchaseStatus` enum, and a `ValidateReceipt` method that doesn't exist. This appears to be leftover from a planned but never implemented feature.

### Fix

Delete the commented-out block (lines 304-323) and the trailing comment on line 332-333. The actual restore validation is handled through `ProcessPendingOrder` → `ValidatePurchaseReceipt` → `ReportPaymentStatusToBalancy` in `UnityPurchaseSystem.cs`.

---

## Summary — Priority Order

| # | Task | Severity | Impact |
|---|------|----------|--------|
| 1 | Cleanup destroys valid `WaitingForStore` entries | Critical | Lost purchases after crash |
| 2 | `ProcessPendingOrder` fails for restores / previous sessions | High | Restore flow drops purchases |
| 7 | `ConfirmPurchase` before validation completes | Medium | Confirms invalid purchases |
| 3 | Duplicate purchase guard disabled | Medium | Duplicate charges possible |
| 4 | `RefreshProductList` is a no-op | Medium | Empty product queries |
| 5 | `GetSubscriptionsInfo` returns fake data | Medium | Wrong subscription status |
| 10 | `Instance` is private, public API unreachable | Medium | API unusable from game code |
| 6 | `LogError` suppressed by `debugMode` | Low-Med | Silent production failures |
| 11 | `ApplyConfig` can index out of bounds | Low | Crash on malformed API data |
| 12 | `debugMode` not configurable (hidden GO) | Low | Cannot toggle logging |
| 8 | Dead restore methods | Low | Code cleanliness |
| 9 | Dead `_validationQueue` field | Low | Code cleanliness |
| 13 | Commented-out restore validation | Low | Code cleanliness |
