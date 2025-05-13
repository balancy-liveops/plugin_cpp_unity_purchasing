# Balancy Payments System - Documentation

## Overview

The Balancy Payments system is a comprehensive solution for implementing in-app purchases in Unity games. Built on top of Unity IAP 4.12.2, it provides additional features to handle common edge cases and simplify the integration process.

## Features

- **Simple API**: Easy-to-use API with callback support for all operations
- **Robust Error Handling**: Gracefully handles failures throughout the purchase flow
- **Purchase Persistence**: Saves purchase state to handle interruptions (e.g., app crashes)
- **Receipt Validation**: Optional server-side validation for improved security
- **Subscription Support**: Complete support for managing subscriptions
- **Cross-Platform**: Works with all major platforms supported by Unity IAP
- **Configurable**: Easy setup through Unity inspector

## System Architecture

The Balancy Payments system consists of several components that work together:

1. **IBalancyPaymentSystem**: Interface defining the contract for payment implementations
2. **UnityPurchaseSystem**: Implementation of the payment interface using Unity IAP
3. **PendingPurchaseManager**: Handles persistence of purchase state
4. **ReceiptValidator**: Validates purchase receipts with a server
5. **BalancyPaymentManager**: Main manager that ties everything together
6. **BalancyPaymentConfig**: Configuration for the payment system

## Getting Started

### Prerequisites

- Unity 2020.3 or newer
- Unity IAP 4.12.2 package

### Installation

1. Add the Balancy Payments package to your project
2. Setup Unity IAP using the menu: **Balancy → Payments → Setup Project for IAP**
3. Create a payment configuration: **Balancy → Payments → Create Default Configuration**
4. Configure your products in the created configuration asset
5. Add the `BalancyPaymentManager` component to a GameObject in your scene

### Basic Configuration

The `BalancyPaymentConfig` allows you to configure:

- **Products**: Define your IAP products with IDs, types, and store-specific identifiers
- **Initialization Settings**: Auto-initialize on start, auto-finish transactions
- **Validation Settings**: Enable receipt validation and configure validation URLs

## Using the API

### Initialization

```csharp
// Automatic initialization
// Add BalancyPaymentManager to a GameObject and check "Initialize On Awake"

// Manual initialization
BalancyPaymentManager.Instance.Initialize(
    paymentConfig,  // Optional, defaults to the one set in the inspector
    () => { 
        // Success callback
        Debug.Log("Payment system initialized successfully");
    },
    (error) => { 
        // Error callback
        Debug.LogError($"Payment system initialization failed: {error}");
    }
);
```

### Retrieving Products

```csharp
// Get all products
BalancyPaymentManager.Instance.GetProducts(products => {
    foreach (var product in products) {
        Debug.Log($"Product: {product.ProductId}");
        Debug.Log($"  Title: {product.Metadata.LocalizedTitle}");
        Debug.Log($"  Price: {product.Metadata.LocalizedPriceString}");
    }
});

// Get a specific product
BalancyPaymentManager.Instance.GetProduct("com.example.myproduct", product => {
    if (product != null) {
        Debug.Log($"Found product: {product.Metadata.LocalizedTitle}");
    } else {
        Debug.LogWarning("Product not found");
    }
});
```

### Making Purchases

```csharp
BalancyPaymentManager.Instance.PurchaseProduct("com.example.myproduct", result => {
    switch (result.Status) {
        case PurchaseStatus.Success:
            Debug.Log("Purchase successful!");
            // Grant the product to the player
            break;
            
        case PurchaseStatus.Cancelled:
            Debug.Log("Purchase cancelled by user");
            break;
            
        case PurchaseStatus.Failed:
            Debug.LogError($"Purchase failed: {result.ErrorMessage}");
            break;
            
        case PurchaseStatus.Pending:
            Debug.Log("Purchase is in a pending state");
            break;
            
        case PurchaseStatus.AlreadyOwned:
            Debug.Log("Product already owned");
            // Grant the product to the player
            break;
            
        case PurchaseStatus.InvalidProduct:
            Debug.LogError("Invalid product");
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
            Debug.Log($"Restored: {result.ProductId}");
        }
    }
});
```

### Getting Subscription Information

```csharp
BalancyPaymentManager.Instance.GetSubscriptionsInfo(subscriptions => {
    foreach (var subscription in subscriptions) {
        Debug.Log($"Subscription: {subscription.ProductId}");
        Debug.Log($"  Is active: {subscription.IsSubscribed}");
        Debug.Log($"  Expires: {subscription.ExpireDate}");
    }
});
```

### Finishing Transactions

In most cases, transactions are automatically finished by the system. However, you can manually finish them if needed:

```csharp
BalancyPaymentManager.Instance.FinishTransaction(productId, transactionId);
```

## Event-Based API

In addition to callbacks, you can listen for purchase events:

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
    if (result.Status == PurchaseStatus.Success) {
        Debug.Log($"Product purchased: {result.ProductId}");
    }
}

void HandlePurchasesRestored(List<PurchaseResult> results) {
    Debug.Log($"Restored {results.Count} purchases");
}
```

## Receipt Validation

To validate purchases with your server:

1. Enable "Validate Receipts" in your BalancyPaymentConfig
2. Set the "Validation Service URL" to your server endpoint
3. Optionally set a "Validation Secret" for authentication

Your server should return a JSON response with this structure:

```json
{
    "isValid": true,
    "transactionId": "transaction_id_123",
    "productId": "com.example.myproduct",
    "isTestPurchase": false,
    "purchaseTime": 1620000000,
    "errorMessage": "",
    "validationPayload": "optional-data-for-your-use"
}
```

## Edge Cases Handling

The system automatically handles several edge cases:

### App Crashes During Purchase

If the app crashes during a purchase, the system will:
1. Save the pending purchase state to disk
2. Recover the purchase when the app is restarted
3. Validate and finalize the purchase automatically

### Network Failures

The system handles network interruptions by:
1. Storing purchase information locally
2. Retrying validation when connectivity is restored
3. Maintaining purchase state across app sessions

### Receipt Validation Failures

If receipt validation fails, the system:
1. Logs the error with details
2. Notifies through callbacks or events
3. Allows you to decide how to handle the failure (e.g., retry, grant anyway, deny)

### Already Owned Products

When users try to purchase products they already own:
1. The system detects the situation
2. Returns a special status (`PurchaseStatus.AlreadyOwned`)
3. Allows you to handle it gracefully (typically by granting the product)

## Customizing the System

### Creating a Custom Payment System

To create a custom payment system implementation:

1. Implement the `IBalancyPaymentSystem` interface
2. Override the `CreatePaymentSystem` method in `BalancyPaymentManager`:

```csharp
protected override IBalancyPaymentSystem CreatePaymentSystem()
{
    // For example, create a custom payment system for a specific platform
    if (Application.platform == RuntimePlatform.Android) {
        return new MyCustomAndroidPaymentSystem();
    }
    
    // Default to Unity's system
    return UnityPurchaseSystem.Instance;
}
```

### Custom Receipt Validation

You can implement custom receipt validation by:

1. Creating your own validator class
2. Setting it in the manager after initialization:

```csharp
// Create custom validator
var customValidator = new MyCustomValidator(validationUrl, validationSecret, this);

// Set it in BalancyPaymentManager
var managerField = typeof(BalancyPaymentManager).GetField(
    "_receiptValidator", 
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
managerField.SetValue(BalancyPaymentManager.Instance, customValidator);
```

## Testing

### Testing in the Unity Editor

The system uses Unity IAP's fake store in the Editor:

1. Configure test products in your `BalancyPaymentConfig`
2. Run your game in the Editor
3. Make purchases through the fake store UI

### Testing on Device

For testing on real devices:

1. Configure test accounts in the respective app stores:
   - iOS: Use TestFlight and sandbox accounts
   - Google Play: Use test tracks and license testing
   - Amazon: Use test environment
2. Use these test accounts when making purchases

## Troubleshooting

### Common Issues

1. **Products not available**
   - Verify product IDs match those in the store consoles
   - Check that the app is properly configured in the stores
   - Verify the app is signed with the correct keys

2. **Initialization failures**
   - Check Unity IAP is properly installed
   - Verify internet connectivity
   - Check store-specific setup (e.g., Google Play services)

3. **Purchase failures**
   - Enable debug mode on the `BalancyPaymentManager`
   - Check the specific error message
   - Verify internet connection and store account status

### Debugging

Enable debug logs:
1. Select the GameObject with `BalancyPaymentManager`
2. Check "Debug Mode" in the inspector
3. View logs in the Unity Console

## Performance Considerations

The system is designed to be lightweight:

- Minimal overhead over Unity IAP
- Uses file I/O only when necessary (saving pending purchases)
- Most operations are asynchronous and non-blocking

## Platform-Specific Notes

### iOS

- Requires app-specific password for test accounts
- Restore purchases is required by Apple guidelines

### Android (Google Play)

- Google Play billing library is used by Unity IAP
- Handle subscription management through Google Play

### Amazon

- Special considerations for Amazon Appstore
- Different receipt format

## Security Considerations

- Always validate receipts on your server for production builds
- Store validation secrets securely
- Consider implementing additional fraud prevention

## Upgrading

When upgrading the Balancy Payments system:

1. Back up your project
2. Remove the old version
3. Import the new version
4. Update any custom implementations if necessary

## Support

For support with the Balancy Payments system:

- Check the documentation
- Look for examples in the Examples folder
- Contact support@balancy.io

---

© 2025 Balancy Team