# Balancy Payments System

## Overview

The Balancy Payments system provides a unified interface for implementing in-app purchases in Unity games. It wraps the Unity IAP system (version 4.12.2) and provides additional features such as:

- Handling interrupted purchases (e.g., when the app crashes during purchase)
- Persistence of purchase states
- Receipt validation with server
- Subscription management
- Easy to use API with callbacks

## Getting Started

### Prerequisites

- Unity 2020.3 or newer
- Unity IAP 4.12.2 package

### Installation

1. Setup Unity IAP in your project:
   - Go to **Balancy → Payments → Setup Project for IAP** in the Unity menu
   - Wait for the Unity Package Manager to install the IAP package

2. Create a payment configuration:
   - Go to **Balancy → Payments → Create Default Configuration**
   - Save the configuration asset in your project

3. Configure your products:
   - Open the configuration asset
   - Add your products with their IDs, types, and store-specific IDs if needed

4. Add the payment manager to your scene:
   - Create an empty GameObject
   - Add the `BalancyPaymentManager` component
   - Assign your configuration to the manager
   - Check "Initialize On Awake" to initialize automatically

## Usage

### Initialization

If you want to initialize the payment system manually:

```csharp
BalancyPaymentManager.Instance.Initialize(
    paymentConfig,  // Optional: defaults to the one set in the inspector
    () => {
        Debug.Log("Payment system initialized successfully");
    },
    (error) => {
        Debug.LogError($"Failed to initialize payment system: {error}");
    }
);
```

### Getting Products

```csharp
BalancyPaymentManager.Instance.GetProducts(products => {
    foreach (var product in products) {
        Debug.Log($"{product.ProductId}: {product.Metadata.LocalizedPriceString}");
    }
});
```

### Purchasing a Product

```csharp
string productId = "com.example.myproduct";

BalancyPaymentManager.Instance.PurchaseProduct(productId, result => {
    switch (result.Status) {
        case PurchaseStatus.Success:
            Debug.Log("Purchase successful!");
            // Grant the product to the player
            break;
            
        case PurchaseStatus.Cancelled:
            Debug.Log("Purchase was cancelled by the user");
            break;
            
        case PurchaseStatus.Failed:
            Debug.LogError($"Purchase failed: {result.ErrorMessage}");
            break;
            
        case PurchaseStatus.Pending:
            Debug.Log("Purchase is pending");
            break;
    }
});
```

### Restoring Purchases

```csharp
BalancyPaymentManager.Instance.RestorePurchases(results => {
    Debug.Log($"Restored {results.Count} purchases");
    
    foreach (var result in results) {
        if (result.Status == PurchaseStatus.Success) {
            // Grant the product to the player
            Debug.Log($"Restored product: {result.ProductId}");
        }
    }
});
```

### Getting Subscription Information

```csharp
BalancyPaymentManager.Instance.GetSubscriptionsInfo(subscriptions => {
    foreach (var subscription in subscriptions) {
        Debug.Log($"Subscription: {subscription.ProductId}");
        Debug.Log($"  Is subscribed: {subscription.IsSubscribed}");
        Debug.Log($"  Expires: {subscription.ExpireDate}");
        Debug.Log($"  Is auto-renewing: {subscription.IsAutoRenewing}");
    }
});
```

## Advanced Usage

### Receipt Validation

To validate receipts with your server:

1. In your payment configuration, enable "Validate Receipts"
2. Set the "Validation Service URL" to your server endpoint
3. Set the "Validation Secret" for authentication

The validation server should return a JSON response with this format:

```json
{
    "isValid": true,
    "transactionId": "transaction123",
    "productId": "com.example.myproduct",
    "isTestPurchase": false,
    "purchaseTime": 1620000000,
    "validationPayload": "optional-data-for-your-use"
}
```

### Handling Pending Purchases

The system automatically handles pending purchases from previous sessions. When the app starts, it will:

1. Check for any pending purchases
2. Validate them if needed
3. Finalize them and notify through callbacks

### Event System

You can also listen for purchase events:

```csharp
void Start() {
    BalancyPaymentManager.Instance.OnPurchaseCompleted += HandlePurchaseCompleted;
    BalancyPaymentManager.Instance.OnPurchasesRestored += HandlePurchasesRestored;
}

void OnDestroy() {
    if (BalancyPaymentManager.Instance != null) {
        BalancyPaymentManager.Instance.OnPurchaseCompleted -= HandlePurchaseCompleted;
        BalancyPaymentManager.Instance.OnPurchasesRestored -= HandlePurchasesRestored;
    }
}

void HandlePurchaseCompleted(PurchaseResult result) {
    // Process the purchase
}

void HandlePurchasesRestored(List<PurchaseResult> results) {
    // Process restored purchases
}
```

## Troubleshooting

### Common Issues

1. **Products not available**
   - Make sure your product IDs match those in the store console
   - Check that your app is properly configured in the store

2. **Initialization failure**
   - Ensure Unity IAP package is installed
   - Check that your app is properly set up in the app stores

3. **Purchase failures**
   - Enable debug mode on the BalancyPaymentManager to see detailed logs
   - Check for any network issues

### Debug Mode

Enable the "Debug Mode" option in the BalancyPaymentManager component to see detailed logs in the console.

## License

This package is provided as part of the Balancy suite and is subject to the terms of the Balancy license agreement.