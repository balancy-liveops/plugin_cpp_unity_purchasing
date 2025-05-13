# Balancy Payments - Quick Start Guide

This guide will help you quickly integrate the Balancy Payments system into your Unity project.

## 1. Setup

### Prerequisites

- Unity 2020.3 or newer
- Unity IAP 4.12.2 package (installed automatically by Balancy Payments)

### Installation Steps

1. **Set up Unity IAP**:
   - Go to **Balancy → Payments → Setup Project for IAP** in the Unity menu
   - Wait for the package manager to complete the installation

2. **Create configuration**:
   - Go to **Balancy → Payments → Create Default Configuration**
   - Save the configuration asset in your project

3. **Configure products**:
   - Open the created configuration asset
   - Add your IAP products with their IDs and types

4. **Add to scene**:
   - Create an empty GameObject named "BalancyPayments"
   - Add the `BalancyPaymentManager` component
   - Assign your configuration to the "Config" field
   - Enable "Initialize On Awake" for automatic initialization

## 2. Basic Usage

### Initialize the Payment System

```csharp
// Automatically initialized if "Initialize On Awake" is checked
// For manual initialization:
BalancyPaymentManager.Instance.Initialize(
    null, // Use the assigned config
    () => Debug.Log("Initialized successfully"),
    (error) => Debug.LogError($"Initialization failed: {error}")
);
```

### Get Available Products

```csharp
BalancyPaymentManager.Instance.GetProducts(products => {
    foreach (var product in products) {
        Debug.Log($"{product.ProductId}: {product.Metadata.LocalizedPriceString}");
    }
});
```

### Purchase a Product

```csharp
BalancyPaymentManager.Instance.PurchaseProduct("your_product_id", result => {
    if (result.Status == PurchaseStatus.Success) {
        // Grant the product to the player
        Debug.Log("Purchase successful!");
    } else {
        Debug.Log($"Purchase failed: {result.Status} - {result.ErrorMessage}");
    }
});
```

### Restore Purchases

```csharp
BalancyPaymentManager.Instance.RestorePurchases(results => {
    foreach (var result in results) {
        if (result.Status == PurchaseStatus.Success) {
            // Grant the product to the player
            Debug.Log($"Restored: {result.ProductId}");
        }
    }
});
```

## 3. Advanced Features

### Subscribe to Events

```csharp
void OnEnable() {
    BalancyPaymentManager.Instance.OnPurchaseCompleted += HandlePurchaseCompleted;
    BalancyPaymentManager.Instance.OnPurchasesRestored += HandlePurchasesRestored;
}

void OnDisable() {
    if (BalancyPaymentManager.Instance != null) {
        BalancyPaymentManager.Instance.OnPurchaseCompleted -= HandlePurchaseCompleted;
        BalancyPaymentManager.Instance.OnPurchasesRestored -= HandlePurchasesRestored;
    }
}

void HandlePurchaseCompleted(PurchaseResult result) {
    // Handle purchase completion
}

void HandlePurchasesRestored(List<PurchaseResult> results) {
    // Handle restored purchases
}
```

### Server Receipt Validation

1. In your configuration asset:
   - Enable "Validate Receipts"
   - Set "Validation Service URL" to your server endpoint
   - Set "Validation Secret" for authentication

2. Ensure your server returns the correct response format:
   ```json
   {
       "isValid": true,
       "transactionId": "transaction_id",
       "productId": "product_id",
       "isTestPurchase": false
   }
   ```

### Subscription Management

```csharp
BalancyPaymentManager.Instance.GetSubscriptionsInfo(subscriptions => {
    foreach (var subscription in subscriptions) {
        if (subscription.IsSubscribed && !subscription.IsExpired) {
            // Grant subscription benefits
        }
    }
});
```

## 4. Testing

### In Unity Editor

- Run your scene with the BalancyPaymentManager
- Purchases will use Unity's fake store

### On Device

- iOS: Use TestFlight and sandbox accounts
- Google Play: Use license testing
- Amazon: Use test environment

## 5. Debugging

- Select the BalancyPaymentManager in your scene
- Enable "Debug Mode" for detailed logs
- Check the Unity Console for log messages

## 6. Common Issues

- **Products not showing**: Check product IDs match store console
- **Initialization failures**: Verify internet connection and store setup
- **Purchase failures**: Check specific error message and debug logs

## 7. Next Steps

- See the full documentation in `BalancyPaymentsSystem.md`
- Check the example in `Examples/BalancyPaymentExample.cs`

For more help, contact support@balancy.io