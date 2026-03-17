# Balancy Purchasing for Unity

**In-App Purchasing module with Receipt Validation & Reward Delivery, built on Unity IAP v5.**

Balancy Purchasing handles the entire purchase flow — initiating transactions, validating receipts server-side, and granting rewards — on top of the Balancy LiveOps platform.

## Requirements

- Unity **2022.3** or newer
- Unity Purchasing (IAP) **5.0.1** or newer (installed automatically as a dependency)
- Balancy SDK (core) must be integrated in the project
- Products must be defined in the [Balancy Dashboard](https://balancy.dev)

## Features

- **Automatic Initialization** — initializes automatically when Balancy cloud sync completes; no manual setup step required
- **Unity IAP v5 Integration** — uses the modern async/event-based IAP v5 API (`StoreController`, `Order`, `PendingOrder`, etc.)
- **Server-Side Receipt Validation** — receipts are sent to Balancy servers for validation before rewards are granted
- **Automatic Reward Delivery** — on successful validation, Balancy grants items, currencies, or perks defined in the dashboard
- **Pending Purchase Recovery** — interrupted purchases (app crash, network loss) are persisted to disk and retried on next launch
- **Restore Purchases** — explicit restore flow for re-downloading non-consumable/subscription purchases on new devices (required by Apple)
- **Cross-Platform** — supports Apple App Store, Google Play, and all other platforms supported by Unity IAP
- **Product Types** — Consumable, NonConsumable, and Subscription
- **Balancy LiveOps Integration** — works with Store Items, Offers, Offer Groups, and Shop Slots from the Balancy economy system

## Installation

### Via OpenUPM (recommended)

```bash
openupm add co.balancy.unity-purchasing
```

### Via Git URL

1. Open **Window > Package Manager** in Unity
2. Click the **"+"** button
3. Select **"Add package from git URL"**
4. Enter:
   ```
   https://github.com/balancy-liveops/plugin_cpp_unity_purchasing.git
   ```

Unity Purchasing 5.0.1 will be added automatically as a dependency.

## Getting Started

### Step 1: Set Up Balancy SDK

Make sure the core Balancy SDK is integrated and configured in your project via **Tools > Balancy > Config**. Log in, select your game, and generate code / download data.

### Step 2: Define Products in the Balancy Dashboard

Create your in-app products in the [Balancy Dashboard](https://balancy.dev). Each product needs:
- A **product ID** that matches the ID registered in the App Store / Google Play Console
- A **product type** (Consumable, NonConsumable, or Subscription)
- Reward configuration (items, currencies, etc.)

### Step 3: No Additional Configuration Needed

There is **no** editor menu or config window for the purchasing module. Product definitions are fetched automatically from the Balancy API at runtime. Once Balancy SDK syncs with the cloud, the purchasing module initializes itself.

> **Note:** There is no "Tools > Balancy > Purchasing Config" menu item. If older documentation references this, it is outdated. All product configuration is done in the Balancy Dashboard and fetched at runtime.

## Architecture

### Module Structure

```
Assets/BalancyPayments/
├── IBalancyPaymentSystem.cs      # Interface + data types (ProductInfo, PurchaseResult, etc.)
├── BalancyPaymentManager.cs      # Main manager (singleton MonoBehaviour, auto-creates)
├── UnityPurchaseSystem.cs        # Unity IAP v5 implementation
├── PendingPurchaseManager.cs     # Persists interrupted purchases to disk
├── Balancy.Payments.asmdef       # Assembly definition
├── package.json                  # UPM package manifest
└── README.md                     # This file
```

### Key Classes

| Class | Role |
|-------|------|
| `BalancyPaymentManager` | Singleton MonoBehaviour. Entry point for all purchase operations. Auto-creates a hidden `DontDestroyOnLoad` GameObject. |
| `UnityPurchaseSystem` | Implements `IBalancyPaymentSystem` using Unity IAP v5 APIs. Handles store communication. |
| `PendingPurchaseManager` | Persists pending purchases to `balancy_pending_purchases.json` in `Application.persistentDataPath`. Recovers interrupted purchases. |
| `IBalancyPaymentSystem` | Interface that abstracts the payment system. Could be replaced with a custom implementation via `BalancyPaymentManager.SetPaymentSystem()`. |

### Namespace

All types are in the `Balancy.Payments` namespace.

## Initialization Flow

Initialization is **fully automatic**. Here is the sequence:

1. `BalancyPaymentManager.Init()` runs via `[RuntimeInitializeOnLoadMethod]` and subscribes to `Balancy.Controller.OnCloudSynced`.
2. When Balancy cloud sync completes, `OnCloudSynced` fires and calls `Initialize()`.
3. `Initialize()` waits for a payment system to be set (the `UnityPurchaseSystem` sets itself via `BalancyPaymentManager.SetPaymentSystem(this)` after connecting to the store).
4. `UnityPurchaseSystem.InitializeIAPv5()`:
   - Calls `UnityIAPServices.StoreController()` to get the store controller
   - Registers event handlers (`OnPurchasePending`, `OnPurchaseConfirmed`, `OnPurchaseFailed`, `OnProductsFetched`, `OnPurchasesFetched`)
   - Calls `StoreController.Connect()` (async)
   - Calls `StoreController.FetchProducts()` with product definitions from the Balancy API
5. When products are fetched (`OnProductsFetched`), the system calls `StoreController.FetchPurchases()` to retrieve any existing purchases.
6. When purchases are fetched (`OnPurchasesFetched`), pending orders are processed, initialization completes, and:
   - `Balancy.Actions.Purchasing.SetHardPurchaseCallback()` is registered
   - `Balancy.Actions.Purchasing.SetRestorePurchasesCallback()` is registered
   - `Balancy.Callbacks.OnPaymentIsReady` is invoked

### Listening for Payment Ready

```csharp
// Subscribe before Balancy initializes
Balancy.Callbacks.OnPaymentIsReady += () =>
{
    Debug.Log("Payment system is ready!");
    // Safe to initiate purchases, query product info, etc.
};
```

## Purchase Flow

### How Purchases Work

Purchases are triggered through the Balancy API — **not** by calling `BalancyPaymentManager` directly. When a "hard purchase" (real-money IAP) is needed, Balancy internally calls the registered `HardPurchaseCallback`, which the `BalancyPaymentManager` handles automatically.

**Initiating a purchase from game code:**

```csharp
// Purchase a shop slot (recommended for most use cases)
Balancy.API.InitPurchaseShop(shopSlot, (success, error) =>
{
    Debug.Log($"Shop slot purchase complete: success={success}, error={error}");
});

// Purchase an offer
Balancy.API.InitPurchaseOffer(offerInfo, (success, error) =>
{
    Debug.Log($"Offer purchase complete: success={success}, error={error}");
});

// Purchase from an offer group
Balancy.API.InitPurchaseOffer(offerGroupInfo, storeItem, (success, error) =>
{
    Debug.Log($"Offer group purchase complete: success={success}, error={error}");
});

// Purchase a store item directly (deprecated — prefer InitPurchaseShop or InitPurchaseOffer)
Balancy.API.InitPurchase(storeItem, (success, error) =>
{
    Debug.Log($"Purchase complete: success={success}, error={error}");
});
```

These methods automatically determine whether the purchase is "hard" (real money via IAP) or "soft" (in-game currency). For hard purchases, the BalancyPayments module handles the store interaction. For soft purchases, the transaction is processed locally.

**Additional soft purchase methods** (for manual control, most developers won't need these):

```csharp
Balancy.API.SoftPurchaseShopSlot(shopSlot);       // Soft-purchase a shop slot
Balancy.API.SoftPurchaseGameOffer(offerInfo);      // Soft-purchase an offer
Balancy.API.SoftPurchaseGameOfferGroup(offerGroupInfo, storeItem); // Soft-purchase from offer group
```

**The internal flow (handled automatically):**

1. Game code calls `Balancy.API.InitPurchase*()` (or player taps a store item in the Balancy WebView UI)
2. Balancy determines if this is a "hard" (real-money) or "soft" (in-game currency) purchase
3. For hard purchases, Balancy calls the `HardPurchaseCallback` with a `BalancyProductInfo`
4. `BalancyPaymentManager.PurchaseProduct()` is called internally
5. A `PendingPurchase` record is created and saved to disk
6. `StoreController.PurchaseProduct()` is called (native store dialog appears)
7. On success: `OnPurchasePending` fires → receipt is extracted → `ReportPaymentStatusToBalancy()` is called for server-side validation → `StoreController.ConfirmPurchase()` confirms the transaction
8. On failure: `OnPurchaseFailed` fires → status is reported to Balancy → pending purchase is cleaned up
9. On successful validation: Balancy grants the configured rewards automatically

### Purchase Types

The `BalancyProductInfo.PurchaseType` enum indicates what triggered the purchase:

| Type | Description |
|------|-------------|
| `StoreItem` | A direct store item purchase |
| `Offer` | A time-limited or targeted offer |
| `OfferGroup` | A grouped offer |
| `ShopSlot` | A shop slot purchase |

### Listening for Purchase Results

```csharp
// Called after a store item purchase is validated and rewards granted
Balancy.Callbacks.OnHardPurchasedStoreItem += (PaymentInfo paymentInfo, StoreItem storeItem) =>
{
    Debug.Log($"Purchased store item: {storeItem.Name}, Price: {paymentInfo.Price} {paymentInfo.Currency}");
};

// Called after an offer purchase
Balancy.Callbacks.OnHardPurchasedOffer += (PaymentInfo paymentInfo, GameOffer offer) =>
{
    Debug.Log($"Purchased offer: {offer}");
};

// Called after an offer group purchase
Balancy.Callbacks.OnHardPurchasedOfferGroup += (PaymentInfo paymentInfo, GameOfferGroup group, StoreItem item) =>
{
    Debug.Log($"Purchased offer group item");
};

// Called after a shop slot purchase
Balancy.Callbacks.OnHardPurchasedShopSlot += (PaymentInfo paymentInfo, Slot slot) =>
{
    Debug.Log($"Purchased shop slot");
};
```

## Product Types

```csharp
public enum ProductType
{
    Consumable = 1,       // Can be purchased multiple times (e.g., gems, coins)
    NonConsumable = 2,    // Purchased once, persists forever (e.g., remove ads)
    Subscription = 3      // Recurring purchase (e.g., VIP pass)
}
```

## Querying Product Information

Product information is queried through the `Balancy.API` — **not** through `BalancyPaymentManager` directly (its singleton `Instance` is private and internal).

### Get All Products

```csharp
Balancy.API.GetProducts(response =>
{
    if (response.Success)
    {
        foreach (var product in response.Products)
        {
            Debug.Log($"Product: {product.Name}");
            Debug.Log($"  Product ID: {product.ProductId}");
            Debug.Log($"  Price: {product.Price}");
            Debug.Log($"  Description: {product.Description}");
            Debug.Log($"  Localized Name: {product.LocalizedName}");
            Debug.Log($"  Localized Description: {product.LocalizedDescription}");
        }
    }
    else
    {
        Debug.LogError($"Failed to get products: {response.ErrorMessage}");
    }
});
```

### Get a Specific Product

```csharp
Balancy.API.GetProduct("com.mygame.gems100", response =>
{
    if (response.Success && response.Product != null)
    {
        var product = response.Product;
        Debug.Log($"Product: {product.Name}");
        Debug.Log($"  Price: {product.Price}");
        Debug.Log($"  Product ID: {product.PlatformProductId}");
    }
    else
    {
        Debug.LogError($"Product not found: {response.ErrorMessage}");
    }
});
```

> **Note:** `Balancy.API.GetProduct()` internally calls `Balancy.API.GetProducts()` and filters by product ID.

### Displaying Localized Prices in Custom UI

If you need to display store-localized prices (from Apple/Google) in your own UI rather than the Balancy WebView, register a product info callback:

```csharp
Balancy.Actions.Purchasing.SetGetHardPurchaseInfoCallback((productId, callback) =>
{
    Balancy.API.GetProduct(productId, response =>
    {
        var info = new Balancy.Actions.Purchasing.HardProductInfo();
        if (response.Success && response.Product != null)
        {
            var product = response.Product;
            info.LocalizedTitle = product.LocalizedName ?? product.Name;
            info.LocalizedDescription = product.LocalizedDescription ?? product.Description;
            info.LocalizedPriceString = $"${product.Price:F2}";
            info.LocalizedPrice = product.Price;
            info.IsoCurrencyCode = "USD";
        }
        callback?.Invoke(info);
    });
});
```

> **Note:** The Balancy WebView UI uses this callback internally to display product prices. If you use the Balancy WebView for your store UI, this is handled for you.

## Restore Purchases

Restore purchases must be **triggered explicitly** by the developer. It is **not automatic**. Apple requires iOS apps to provide a "Restore Purchases" button for non-consumable and subscription products.

### How It Works

When the payment system initializes, it registers a restore callback with Balancy internally:
```csharp
Balancy.Actions.Purchasing.SetRestorePurchasesCallback(RestorePurchases);
```

Trigger restore from your UI by calling `Balancy.API.RestorePurchases()`:

```csharp
// Attach this to a "Restore Purchases" button in your UI
public void OnRestoreButtonClicked()
{
    Balancy.API.RestorePurchases();
}
```

This invokes the registered restore callback inside `BalancyPaymentManager`, which calls `UnityPurchaseSystem.RestorePurchases()` under the hood.

### Restore vs. Pending Purchase Recovery

These are two different mechanisms:

| Feature | Restore Purchases | Pending Purchase Recovery |
|---------|-------------------|--------------------------|
| **Purpose** | Re-download past purchases on a new device or after reinstall | Recover purchases interrupted by crash/network loss |
| **Trigger** | Explicit — user taps "Restore Purchases" button | Automatic — runs on app startup and resume |
| **Scope** | Non-consumable and subscription products | Any purchase that was in-flight |
| **Storage** | Handled by the store (Apple/Google) | Local file: `balancy_pending_purchases.json` |

### Listening for Restore Events

Subscribe to the `OnPurchasesRestored` event on the manager to be notified when restore completes. Since `BalancyPaymentManager.Instance` is private, listen through Balancy callbacks instead:

```csharp
// Listen for individual restored purchases via the standard purchase callbacks
Balancy.Callbacks.OnHardPurchasedStoreItem += (paymentInfo, storeItem) =>
{
    Debug.Log($"Restored/purchased: {storeItem.Name}");
};
```

## Pending Purchase Recovery

The `PendingPurchaseManager` handles purchases that were interrupted (e.g., app crash during validation).

### How It Works

1. When a purchase is initiated, a `PendingPurchase` record is saved to disk with status `WaitingForStore`.
2. When the store confirms the purchase, the status changes to `ProcessingValidation` and the receipt/transaction data is stored.
3. When validation succeeds, the pending purchase is removed.
4. If the app crashes during step 2 or 3, the pending purchase persists on disk.
5. On next launch, `ProcessPendingPurchases()` re-validates any purchases stuck in `ProcessingValidation` status.
6. On app resume from background, `ProcessPendingPurchases()` is called again.

### Pending Purchase States

```csharp
public enum PendingStatus
{
    WaitingForStore = 0,       // Purchase initiated, waiting for store response
    ProcessingValidation = 1,  // Store confirmed, waiting for server validation
    Failed = 3                 // Purchase failed (kept for tracking)
}
```

### Storage

Pending purchases are stored in JSON format at:
```
{Application.persistentDataPath}/balancy_pending_purchases.json
```

Old entries (> 30 days) are automatically cleaned up on app startup.

## Subscription Info

> **Known Limitation:** The `GetSubscriptionsInfo()` method is not yet implemented — it returns placeholder data with hardcoded values. Do not rely on it for subscription status. Verify subscription state through your backend or the Balancy Dashboard. This is planned for improvement.

## Receipt Validation

Receipt validation happens automatically during the purchase flow — developers do not need to call validation methods manually:

1. After the store confirms a purchase, `ReportPaymentStatusToBalancy()` is called internally.
2. This constructs a `PaymentInfo` object with the product ID, receipt, currency, price, and order ID.
3. `Balancy.API.FinalizedHardPurchase()` sends the receipt to Balancy servers for validation.
4. In the Unity Editor, validation is **skipped** (`requireValidation = false`) to allow testing without real receipts.
5. In builds, validation is **enforced** (`requireValidation = true`).
6. On successful validation, `productInfo.ReportThePurchase(paymentInfo)` is called, which triggers the appropriate `Balancy.Callbacks.OnHardPurchased*` event and grants rewards.

### PaymentInfo Fields

| Field | Type | Description |
|-------|------|-------------|
| `ProductId` | `string` | Store product identifier |
| `Receipt` | `string` | Raw receipt string from the store |
| `Currency` | `string` | ISO currency code (e.g., "USD") |
| `Price` | `float` | Localized price |
| `OrderId` | `string` | Transaction/order ID |
| `PriceUSD` | `float` | Price in USD (for analytics) |

## Purchase Status Codes

```csharp
public enum PurchaseStatus
{
    Success,          // Purchase completed and validated
    Failed,           // Purchase failed (generic)
    Pending,          // Purchase is pending (deferred payment, etc.)
    Cancelled,        // User cancelled the purchase
    AlreadyOwned,     // Product already owned (duplicate transaction)
    InvalidProduct    // Product not found or unavailable
}
```

### Status Mapping to Balancy

| PurchaseStatus | Balancy Result |
|----------------|---------------|
| `Success` | `PurchaseResult.Success` |
| `Failed`, `AlreadyOwned`, `InvalidProduct` | `PurchaseResult.Failed` |
| `Pending` | `PurchaseResult.Pending` |
| `Cancelled` | `PurchaseResult.Cancelled` |

## Error Handling

### Initialization Failures

If the payment system fails to initialize (e.g., no network, store unavailable), the error is logged. All purchase/query methods call `EnsureInitialized()`, which will attempt re-initialization before proceeding. If re-initialization fails, error callbacks are invoked with an empty result.

### Purchase Failures

Failed purchases are handled through the `OnPurchaseFailed` event from Unity IAP v5. The failure reason is mapped to a `PurchaseStatus`:

| Unity IAP Failure Reason | Mapped Status |
|--------------------------|---------------|
| `UserCancelled` | `Cancelled` |
| `DuplicateTransaction` | `AlreadyOwned` |
| `ProductUnavailable` | `InvalidProduct` |
| All others | `Failed` |

### Debug Logging

The `BalancyPaymentManager` has a `debugMode` field (default: `true`). When enabled, purchase flow events are logged with the `[BalancyPayments]` prefix. Set to `false` in production to reduce log noise.

## Custom Payment System

You can replace the Unity IAP implementation with a custom payment system:

```csharp
public class MyCustomPaymentSystem : IBalancyPaymentSystem
{
    public void Initialize(Action onInitialized, Action<string> onInitializeFailed)
    {
        // Your initialization logic
        onInitialized?.Invoke();
    }

    public void PurchaseProduct(Balancy.Actions.BalancyProductInfo productInfo)
    {
        // Your purchase logic
    }

    public void RestorePurchases(Action<List<PurchaseResult>> onRestoreComplete)
    {
        // Your restore logic
    }

    // ... implement remaining interface methods
}

// Register before Balancy cloud sync completes
BalancyPaymentManager.SetPaymentSystem(new MyCustomPaymentSystem());
```

## Conditional Compilation

The module supports the following preprocessor defines:

| Define | Purpose |
|--------|---------|
| `UNITY_PURCHASING` | Automatically defined when Unity Purchasing >= 4.0.0 is present (via asmdef `versionDefines`) |
| `NO_UNITY_PURCHASING` | Define this to completely exclude `UnityPurchaseSystem` from compilation (e.g., for server builds) |

## Platform-Specific Behavior

### iOS (Apple App Store)
- Restore Purchases is required by Apple — provide a UI button
- Receipts are validated server-side through Balancy
- `StoreController.RestoreTransactions()` triggers the restore flow

### Android (Google Play)
- Restore works the same way via `StoreController.RestoreTransactions()`
- Receipts are validated server-side through Balancy

### Unity Editor
- Receipt validation is **skipped** to allow testing without real store receipts
- Purchases can be tested using the Unity IAP fake store

## Known Limitations

1. **Subscription info returns placeholder data** — `GetSubscriptionsInfo()` does not yet read real subscription status from the store. Use your backend or Balancy Dashboard for accurate subscription state.
2. **AutoFinishTransactions not yet configurable** — the option to control transaction auto-finishing is planned but not implemented.
3. **Duplicate purchase guard is disabled** — rapidly initiating the same purchase can create duplicate pending entries. The guard code exists but is currently commented out.

## Troubleshooting

### Payment system not initializing
- Ensure Balancy SDK is properly configured (Tools > Balancy > Config)
- Verify `Balancy.Controller.OnCloudSynced` fires (check network and credentials)
- Check Unity Console for `[BalancyPayments]` logs

### Products not showing up
- Verify product IDs in the Balancy Dashboard match the store product IDs exactly
- Ensure products are approved/active in the App Store / Google Play Console
- Check that `Balancy.Callbacks.OnPaymentIsReady` has fired before querying products

### Purchases failing
- Check that the device has a valid store account signed in
- Verify receipt validation is working on your Balancy backend
- Check `PurchaseStatus` and `ErrorMessage` in the `PurchaseResult`

### Pending purchases not recovering
- Verify `balancy_pending_purchases.json` exists in `Application.persistentDataPath`
- Note: purchases in `WaitingForStore` status are cleaned up on startup
- Only `ProcessingValidation` purchases are re-validated on restart

## Balancy.API Purchasing Methods Reference

| Method | Description |
|--------|-------------|
| `API.InitPurchaseShop(shopSlot, callback)` | Purchase a shop slot (handles both hard and soft prices) |
| `API.InitPurchaseOffer(offerInfo, callback)` | Purchase a single offer |
| `API.InitPurchaseOffer(offerGroupInfo, storeItem, callback)` | Purchase from an offer group |
| `API.InitPurchase(storeItem, callback)` | Direct store item purchase (**deprecated** — prefer `InitPurchaseShop` or `InitPurchaseOffer`) |
| `API.RestorePurchases()` | Trigger restore of previously purchased products |
| `API.GetProducts(callback)` | Get all products defined in the Balancy Dashboard |
| `API.GetProduct(productId, callback)` | Get a specific product by ID |
| `API.FinalizedHardPurchase(...)` | Report a completed store transaction for server-side validation (called internally by BalancyPayments) |
| `API.SoftPurchaseShopSlot(shopSlot)` | Soft-purchase a shop slot with in-game currency |
| `API.SoftPurchaseGameOffer(offerInfo)` | Soft-purchase an offer |
| `API.SoftPurchaseGameOfferGroup(offerGroupInfo, storeItem)` | Soft-purchase from an offer group |

## Data Types Reference

### ProductInfo

```csharp
public class ProductInfo
{
    public string ProductId;           // Store product identifier
    public string StoreSpecificId;     // Platform-specific ID (if different)
    public ProductType Type;           // Consumable, NonConsumable, or Subscription
    public ProductMetadata Metadata;   // Localized title, description, price
    public bool IsAvailable;           // Whether the product can be purchased
    public object RawProductData;      // Underlying Unity IAP Product object
}
```

### ProductMetadata

```csharp
public class ProductMetadata
{
    public string LocalizedTitle;         // Localized product name
    public string LocalizedDescription;   // Localized product description
    public string LocalizedPriceString;   // Formatted price string (e.g., "$4.99")
    public decimal LocalizedPrice;        // Numeric price in local currency
    public string IsoCurrencyCode;        // ISO 4217 currency code (e.g., "USD")
}
```

### PurchaseResult

```csharp
public class PurchaseResult
{
    public PurchaseStatus Status;      // Success, Failed, Pending, Cancelled, etc.
    public string ProductId;           // Store product identifier
    public PurchaseReceipt Receipt;    // Receipt data for validation
    public string ErrorMessage;        // Error details (if failed)
    public string TransactionId;       // Store transaction ID
    public string CurrencyCode;        // ISO currency code
    public float Price;                // Localized price
}
```

### PurchaseReceipt

```csharp
public class PurchaseReceipt
{
    public string ProductId;           // Store product identifier
    public string TransactionId;       // Store transaction ID
    public string Receipt;             // Raw receipt string
    public string Store;               // Store name (e.g., "AppleAppStore", "GooglePlay")
    public DateTime PurchaseTime;      // When the purchase occurred
    public object RawReceipt;          // Store-specific receipt data
}
```

### SubscriptionInfo

```csharp
public class SubscriptionInfo
{
    public string ProductId;
    public DateTime PurchaseDate;
    public DateTime ExpireDate;
    public bool IsSubscribed;
    public bool IsExpired;
    public bool IsCancelled;
    public bool IsFreeTrial;
    public bool IsAutoRenewing;
    public TimeSpan RemainingTime;
    public string IntroductoryPrice;
    public TimeSpan IntroductoryPricePeriod;
    public long IntroductoryPriceCycles;
}
```

## Full Example: Complete Integration

```csharp
using UnityEngine;

public class PurchaseExample : MonoBehaviour
{
    void Start()
    {
        // 1. Listen for payment system ready
        Balancy.Callbacks.OnPaymentIsReady += OnPaymentReady;

        // 2. Listen for purchase results
        Balancy.Callbacks.OnHardPurchasedStoreItem += OnStoreItemPurchased;
    }

    void OnPaymentReady()
    {
        Debug.Log("Payment system ready");
    }

    void OnStoreItemPurchased(Balancy.Core.PaymentInfo paymentInfo, Balancy.StoreItem storeItem)
    {
        // 3. Called after purchase is validated and rewards are granted by Balancy
        Debug.Log($"Purchased {storeItem.Name} for {paymentInfo.Price} {paymentInfo.Currency}");
    }

    // 4. Initiate a purchase (e.g., from a custom store UI button)
    public void OnBuyButtonClicked(Balancy.StoreItem storeItem)
    {
        Balancy.API.InitPurchase(storeItem, (success, error) =>
        {
            if (!success)
                Debug.LogError($"Purchase failed: {error}");
        });
    }

    // 5. Restore purchases (attach to a "Restore Purchases" UI button — required by Apple)
    public void OnRestoreButtonClicked()
    {
        Balancy.API.RestorePurchases();
    }

    void OnDestroy()
    {
        Balancy.Callbacks.OnPaymentIsReady -= OnPaymentReady;
        Balancy.Callbacks.OnHardPurchasedStoreItem -= OnStoreItemPurchased;
    }
}
```

## Documentation

- [Balancy Docs](https://en.docsv2.balancy.dev)
- [Release Notes](https://en.docsv2.balancy.dev/release_notes/)

## Support

- [GitHub Issues](https://github.com/balancy-liveops/plugin_cpp_unity_purchasing/issues)
- Email: [contact@balancy.co](mailto:contact@balancy.co)
- [Discord Community](https://discord.gg/balancy)

## License

This package is licensed under the MIT License — see the LICENSE file for details.
