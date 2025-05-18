// UnityPurchaseSystem.cs

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using Unity.Services.Core;
using Unity.Services.Core.Environments;

namespace Balancy.Payments
{
    /// <summary>
    /// Implementation of the Balancy payment system using Unity IAP 4.12.2
    /// </summary>
    public class UnityPurchaseSystem : IBalancyPaymentSystem, IDetailedStoreListener
    {
        #region Private Fields

        private IStoreController _storeController;
        private IExtensionProvider _extensionProvider;
        private IAppleExtensions _appleExtensions;
        private IGooglePlayStoreExtensions _googlePlayExtensions;
        private IAmazonExtensions _amazonExtensions;
        
        private bool _isInitializing;
        private bool _isInitialized;
        private Action _onInitialized;
        private Action<string> _onInitializeFailed;
        
        private List<ProductInfo> _cachedProducts = new List<ProductInfo>();
        private Action<List<PurchaseResult>> _restorePurchasesCallback;
        private List<PurchaseResult> _restoredPurchases = new List<PurchaseResult>();
        
        private static UnityPurchaseSystem _instance;
        private ConfigurationBuilder _builder;

        class ProductPublicInfo
        {
            public string ProductId;
            public ProductType Type;
            public string StoreSpecificId = null;
        }
        
        private List<ProductPublicInfo> _products = new List<ProductPublicInfo>();
        
        // Store our product definitions
        private Dictionary<string, ProductDefinition> _productDefinitions = new Dictionary<string, ProductDefinition>();
        
        private PendingPurchaseManager _pendingPurchaseManager => PendingPurchaseManager.Instance;
        
        #endregion

        #region Public Properties
        
        /// <summary>
        /// Get the singleton instance
        /// </summary>
        internal static UnityPurchaseSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new UnityPurchaseSystem();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Environment for Unity Gaming Services
        /// </summary>
        public string UnityEnvironment { get; set; } = "production";
        
        /// <summary>
        /// Whether to use the UDP (Unity Distribution Portal) store
        /// </summary>
        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize the payment system
        /// </summary>
        public async void Initialize(Action onInitialized, Action<string> onInitializeFailed)
        {
            if (_isInitialized)
            {
                onInitialized?.Invoke();
                return;
            }

            if (_isInitializing)
            {
                _onInitialized += onInitialized;
                if (onInitializeFailed != null)
                {
                    _onInitializeFailed += onInitializeFailed;
                }
                return;
            }

            _isInitializing = true;
            _onInitialized = onInitialized;
            _onInitializeFailed = onInitializeFailed;

            try
            {
                // Initialize Unity Gaming Services
                var options = new InitializationOptions()
                    .SetEnvironmentName(UnityEnvironment);
                
                await UnityServices.InitializeAsync(options);
                
                // Create a builder for IAP
                var module = GetPurchasingModule();
                _builder = ConfigurationBuilder.Instance(module);

                foreach (var productInfo in _products)
                    AssignProductDefinition(productInfo);
                
                // Process any pending purchases from previous sessions
                // ProcessPendingPurchases();
                
                // Initialize Unity Purchasing
                UnityPurchasing.Initialize(this, _builder);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize Unity Purchasing: {ex.Message}");
                _isInitializing = false;
                _onInitializeFailed?.Invoke(ex.Message);
                _onInitializeFailed = null;
                _onInitialized = null;
            }
        }

        /// <summary>
        /// Add a product definition
        /// </summary>
        public void AddProduct(string productId, ProductType type, string storeSpecificId = null)
        {
            _products.Add(new ProductPublicInfo
            {
                ProductId = productId,
                Type = type,
                StoreSpecificId = storeSpecificId
            });
        }
        
        private void AssignProductDefinition(ProductPublicInfo productInfo)
        {
            string productId = productInfo.ProductId;
            ProductType type = productInfo.Type;
            string storeSpecificId = productInfo.StoreSpecificId;
            
            if (_isInitialized)
            {
                Debug.LogWarning("Cannot add products after initialization. Please add products before calling Initialize().");
                return;
            }

            if (string.IsNullOrEmpty(storeSpecificId))
            {
                storeSpecificId = productId;
            }

            ProductDefinition def;
            IDs ids = null;
            
            if (productId != storeSpecificId)
            {
                ids = new IDs();
                ids.Add(storeSpecificId, GetAppStore());
            }

            UnityEngine.Purchasing.ProductType unityType;
            switch (type)
            {
                case ProductType.Consumable:
                    unityType = UnityEngine.Purchasing.ProductType.Consumable;
                    break;
                case ProductType.NonConsumable:
                    unityType = UnityEngine.Purchasing.ProductType.NonConsumable;
                    break;
                case ProductType.Subscription:
                    unityType = UnityEngine.Purchasing.ProductType.Subscription;
                    break;
                default:
                    unityType = UnityEngine.Purchasing.ProductType.Consumable;
                    break;
            }

            def = new ProductDefinition(productId, storeSpecificId, unityType);
            _productDefinitions[productId] = def;
            
            if (_builder != null)
            {
                if (ids != null)
                    _builder.AddProduct(productId, unityType, ids);
                else
                    _builder.AddProduct(productId, unityType);
            }
        }

        /// <summary>
        /// Get information about all products
        /// </summary>
        public void GetProducts(Action<List<ProductInfo>> callback)
        {
            if (!IsInitialized())
            {
                Initialize(() => GetProducts(callback), (error) => callback?.Invoke(new List<ProductInfo>()));
                return;
            }

            if (_cachedProducts.Count > 0)
            {
                callback?.Invoke(_cachedProducts);
                return;
            }

            RefreshProductList();
            callback?.Invoke(_cachedProducts);
        }

        /// <summary>
        /// Get information about a specific product
        /// </summary>
        public void GetProduct(string productId, Action<ProductInfo> callback)
        {
            if (!IsInitialized())
            {
                Initialize(() => GetProduct(productId, callback), 
                    (error) => callback?.Invoke(null));
                return;
            }

            var product = _cachedProducts.FirstOrDefault(p => p.ProductId == productId);
            if (product != null)
            {
                callback?.Invoke(product);
                return;
            }

            RefreshProductList();
            product = _cachedProducts.FirstOrDefault(p => p.ProductId == productId);
            callback?.Invoke(product);
        }

        /// <summary>
        /// Purchase a product
        /// </summary>
        public void PurchaseProduct(Balancy.Actions.BalancyProductInfo productInfo)
        {
            if (!IsInitialized())
            {
                Initialize(() => PurchaseProduct(productInfo), 
                    (error) =>
                    {
                        ReportPaymentStatusToBalancy(productInfo, new PurchaseResult
                        {
                            Status = PurchaseStatus.Failed,
                            ProductId = productInfo.ProductId,
                            ErrorMessage = $"Store not initialized: {error}"
                        });
                    });
                return;
            }

            var productId = productInfo.ProductId;
            if (_storeController == null)
            {
                ReportPaymentStatusToBalancy(productInfo, new PurchaseResult
                {
                    Status = PurchaseStatus.Failed,
                    ProductId = productId,
                    ErrorMessage = "Store controller is null"
                }); 
                return;
            }

            // Check if the product exists
            var product = _storeController.products.WithID(productId);
            if (product == null || !product.availableToPurchase)
            {
                ReportPaymentStatusToBalancy(productInfo, new PurchaseResult
                {
                    Status = PurchaseStatus.InvalidProduct,
                    ProductId = productId,
                    ErrorMessage = "Product not available for purchase"
                });
                return;
            }

            // Create a pending purchase record
            _pendingPurchaseManager.AddPendingPurchase(productInfo);
            
            try
            {
                // Start the purchase flow
                _storeController.InitiatePurchase(product);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error initiating purchase: {ex.Message}");
                
                _pendingPurchaseManager.UpdatePendingPurchase(
                    productId, 
                    null, 
                    null, 
                    null, 
                    PendingStatus.Failed, 
                    ex.Message);
              
                ReportPaymentStatusToBalancy(productInfo, new PurchaseResult
                {
                    Status = PurchaseStatus.Failed,
                    ProductId = productId,
                    ErrorMessage = ex.Message
                });
            }
        }

        /// <summary>
        /// Finish a transaction
        /// </summary>
        public void FinishTransaction(string productId, string transactionId)
        {
            if (!IsInitialized() || _storeController == null)
            {
                Debug.LogWarning("Cannot finish transaction when store is not initialized");
                return;
            }
            
            // Remove the pending purchase
            _pendingPurchaseManager.RemovePendingPurchase(productId, transactionId);
        }

        /// <summary>
        /// Restore previously purchased products
        /// </summary>
        public void RestorePurchases(Action<List<PurchaseResult>> onRestoreComplete)
        {
            if (!IsInitialized())
            {
                Initialize(() => RestorePurchases(onRestoreComplete), 
                    (error) => onRestoreComplete?.Invoke(new List<PurchaseResult>()));
                return;
            }

            if (_storeController == null)
            {
                onRestoreComplete?.Invoke(new List<PurchaseResult>());
                return;
            }

            _restoredPurchases.Clear();
            _restorePurchasesCallback = onRestoreComplete;

            try
            {
                if (Application.platform == RuntimePlatform.IPhonePlayer || 
                    Application.platform == RuntimePlatform.OSXPlayer)
                {
                    // iOS and Mac App Store
                    Debug.Log("Restoring purchases for Apple platform");
                    _appleExtensions.RestoreTransactions(OnAppleRestoreTransactionsComplete);
                }
                else if (Application.platform == RuntimePlatform.Android)
                {
                    // Google Play
                    Debug.Log("Restoring purchases for Android platform");
                    _googlePlayExtensions.RestoreTransactions(OnGooglePlayRestoreTransactionsComplete);
                }
                else
                {
                    // Other platforms don't typically need this
                    Debug.Log("Restore purchases not supported on this platform. Treating as successful.");
                    OnRestoreTransactionsComplete(true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error restoring purchases: {ex.Message}");
                OnRestoreTransactionsComplete(false);
            }
        }

        /// <summary>
        /// Get subscription information
        /// </summary>
        public void GetSubscriptionsInfo(Action<List<SubscriptionInfo>> callback)
        {
            if (!IsInitialized())
            {
                Initialize(() => GetSubscriptionsInfo(callback), 
                    (error) => callback?.Invoke(new List<SubscriptionInfo>()));
                return;
            }

            var subscriptionInfos = new List<SubscriptionInfo>();

            try
            {
                foreach (var product in _storeController.products.all)
                {
                    if (product.definition.type == UnityEngine.Purchasing.ProductType.Subscription && 
                        product.hasReceipt)
                    {
                        string introJson = null;

                        // Get introductory pricing info for the platform
                        if (Application.platform == RuntimePlatform.IPhonePlayer)
                        {
                            var introductoryPrices = _appleExtensions.GetIntroductoryPriceDictionary();
                            if (introductoryPrices != null && 
                                introductoryPrices.TryGetValue(product.definition.storeSpecificId, out var json))
                            {
                                introJson = json;
                            }
                        }
                        else if (Application.platform == RuntimePlatform.Android)
                        {
                            var metadata = product.metadata.GetGoogleProductMetadata();
                            if (metadata != null)
                            {
                                introJson = metadata.originalJson;
                            }
                        }

                        // Create Unity's subscription manager
                        var unitySubscriptionManager = new SubscriptionManager(product, introJson);
                        var unitySubscriptionInfo = unitySubscriptionManager.getSubscriptionInfo();

                        // Convert to our SubscriptionInfo model
                        var subInfo = new SubscriptionInfo
                        {
                            ProductId = unitySubscriptionInfo.getProductId(),
                            PurchaseDate = unitySubscriptionInfo.getPurchaseDate(),
                            ExpireDate = unitySubscriptionInfo.getExpireDate(),
                            IsSubscribed = unitySubscriptionInfo.isSubscribed() == Result.True,
                            IsExpired = unitySubscriptionInfo.isExpired() == Result.True,
                            IsCancelled = unitySubscriptionInfo.isCancelled() == Result.True,
                            IsFreeTrial = unitySubscriptionInfo.isFreeTrial() == Result.True,
                            IsAutoRenewing = unitySubscriptionInfo.isAutoRenewing() == Result.True,
                            RemainingTime = unitySubscriptionInfo.getRemainingTime(),
                            IntroductoryPrice = unitySubscriptionInfo.getIntroductoryPrice(),
                            IntroductoryPricePeriod = unitySubscriptionInfo.getIntroductoryPricePeriod(),
                            IntroductoryPriceCycles = unitySubscriptionInfo.getIntroductoryPricePeriodCycles()
                        };

                        subscriptionInfos.Add(subInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting subscription info: {ex.Message}");
            }

            callback?.Invoke(subscriptionInfos);
        }

        /// <summary>
        /// Check if purchasing is supported on this device
        /// </summary>
        public bool IsPurchasingSupported()
        {
            return _storeController != null && _extensionProvider != null;
        }

        /// <summary>
        /// Check if the store has been initialized
        /// </summary>
        public bool IsInitialized()
        {
            return _isInitialized && _storeController != null && _extensionProvider != null;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Process any pending purchases from previous sessions
        /// </summary>
        public void ProcessPendingPurchases()
        {
            Debug.LogWarning("...ProcessPendingPurchases");
            var pendingPurchases = _pendingPurchaseManager.GetAllPendingPurchases();
            
            foreach (var purchase in pendingPurchases)
            {
                switch (purchase.Status)
                {
                    case PendingStatus.WaitingForStore:
                        
                        break;
                        
                    case PendingStatus.ProcessingValidation:
                        // Purchase was completed at store level but validation was interrupted
                        // We'll need to validate it again when store is initialized
                        ValidatePurchaseReceipt(purchase);
                        break;
                        
                    case PendingStatus.ReadyToFinalize:
                        // Purchase was completed and validated but not finalized
                        // We'll need to grant the purchase and finalize it
                        // NotifyPurchaseComplete(purchase);
                        break;
                        
                    case PendingStatus.Failed:
                        // Keep for record but don't process
                        break;
                }
            }
        }

        /// <summary>
        /// Notify that a purchase is complete
        /// </summary>
        private void ValidatePurchaseReceipt(PendingPurchase purchase)
        {
            Debug.Log($"Purchase completed for {purchase.ProductInfo.ProductId}");
            
            // Create receipt for callback
            var receipt = new PurchaseReceipt
            {
                ProductId = purchase.ProductInfo.ProductId,
                TransactionId = purchase.TransactionId,
                Receipt = purchase.Receipt,
                Store = purchase.Store,
                PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(purchase.Timestamp).DateTime
            };

            // Create result for callback
            var result = new PurchaseResult
            {
                Status = PurchaseStatus.Success,
                ProductId = purchase.ProductInfo.ProductId,
                Receipt = receipt,
                TransactionId = purchase.TransactionId,
                Price = purchase.Price,
                CurrencyCode = purchase.CurrencyCode
            };
            
            ReportPaymentStatusToBalancy(purchase.ProductInfo, result);
        }

        public void ReportPaymentStatusToBalancy(Actions.BalancyProductInfo productInfo, PurchaseResult result)
        {
            var purchaseInfo = new Actions.PurchaseInfo
            {
                ProductId = productInfo?.ProductId,
                Receipt = result.Receipt?.Receipt,
                CurrencyCode = result.CurrencyCode,
                Price = result.Price,
                TransactionId = result.TransactionId,
                ErrorMessage = result.ErrorMessage
            };
                
            Balancy.API.FinalizedHardPurchase(ConvertStatusToResult(result.Status), productInfo, purchaseInfo, (validationSuccess, removeFromPending) =>
            {
                if (validationSuccess)
                {
                    var product = _storeController.products.WithID(productInfo?.ProductId);
                    if (product != null)
                    {
                        Debug.LogError("Confirm pending purchase");
                        _storeController.ConfirmPendingPurchase(product);
                    }
                    
                    _pendingPurchaseManager.RemovePendingPurchase(purchaseInfo.ProductId, purchaseInfo.TransactionId);
                    //TODO report to apple for claiming
                }
                else
                {
                    if (removeFromPending)
                        _pendingPurchaseManager.RemovePendingPurchase(purchaseInfo.ProductId, purchaseInfo.TransactionId);
                    else
                    {
                        //Do nothing
                        // _pendingPurchaseManager.UpdatePendingPurchaseStatus(purchaseInfo.TransactionId,
                        //     PendingStatus.ProcessingValidation);
                    }
                }
            });
        }
        
        private static Actions.PurchaseResult ConvertStatusToResult(PurchaseStatus status)
        {
            switch (status)
            {
                case PurchaseStatus.Success:
                    return Actions.PurchaseResult.Success;
                case PurchaseStatus.Failed:
                case PurchaseStatus.AlreadyOwned:
                case PurchaseStatus.InvalidProduct:
                    return Actions.PurchaseResult.Failed;
                case PurchaseStatus.Pending:
                    return Actions.PurchaseResult.Pending;
                case PurchaseStatus.Cancelled:
                    return Actions.PurchaseResult.Cancelled;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }

        /// <summary>
        /// Refresh the list of products
        /// </summary>
        private void RefreshProductList()
        {
            _cachedProducts.Clear();
            
            if (_storeController == null || _storeController.products == null)
            {
                return;
            }

            foreach (var unityProduct in _storeController.products.all)
            {
                // Create product metadata
                var metadata = new ProductMetadata
                {
                    LocalizedTitle = unityProduct.metadata.localizedTitle,
                    LocalizedDescription = unityProduct.metadata.localizedDescription,
                    LocalizedPriceString = unityProduct.metadata.localizedPriceString,
                    LocalizedPrice = unityProduct.metadata.localizedPrice,
                    IsoCurrencyCode = unityProduct.metadata.isoCurrencyCode
                };

                // Determine product type
                ProductType productType;
                switch (unityProduct.definition.type)
                {
                    case UnityEngine.Purchasing.ProductType.Consumable:
                        productType = ProductType.Consumable;
                        break;
                    case UnityEngine.Purchasing.ProductType.NonConsumable:
                        productType = ProductType.NonConsumable;
                        break;
                    case UnityEngine.Purchasing.ProductType.Subscription:
                        productType = ProductType.Subscription;
                        break;
                    default:
                        productType = ProductType.Consumable;
                        break;
                }

                // Create our product info
                var productInfo = new ProductInfo
                {
                    ProductId = unityProduct.definition.id,
                    StoreSpecificId = unityProduct.definition.storeSpecificId,
                    Type = productType,
                    Metadata = metadata,
                    IsAvailable = unityProduct.availableToPurchase,
                    RawProductData = unityProduct
                };

                _cachedProducts.Add(productInfo);
            }
        }

        /// <summary>
        /// Get the appropriate purchasing module based on platform and settings
        /// </summary>
        private IPurchasingModule GetPurchasingModule()
        {
            var module = StandardPurchasingModule.Instance();
            
#if UNITY_EDITOR
            // Configure the module based on platform-specific needs
            module.useFakeStoreUIMode = FakeStoreUIMode.StandardUser;
            module.useFakeStoreAlways = true;
#endif
            return module;
        }

        /// <summary>
        /// Get the app store for the current platform
        /// </summary>
        private AppStore GetAppStore()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                return AppStore.GooglePlay;
            }
            
            if (Application.platform == RuntimePlatform.IPhonePlayer || 
                Application.platform == RuntimePlatform.OSXPlayer)
            {
                return AppStore.AppleAppStore;
            }

            // Default to simulator in editor
            return AppStore.NotSpecified;
        }

        /// <summary>
        /// Called when Apple's restore transactions process completes
        /// </summary>
        private void OnAppleRestoreTransactionsComplete(bool success, string errorMessage)
        {
            Debug.Log($"Apple restore transactions completed. Success: {success} - Error: {errorMessage}");
            OnRestoreTransactionsComplete(success);
        }

        /// <summary>
        /// Called when Google Play's restore transactions process completes
        /// </summary>
        private void OnGooglePlayRestoreTransactionsComplete(bool success, string errorMessage)
        {
            Debug.Log($"Google Play restore transactions completed. Success: {success} - Error: {errorMessage}");
            OnRestoreTransactionsComplete(success);
        }

        /// <summary>
        /// Handle completion of restore transactions
        /// </summary>
        private void OnRestoreTransactionsComplete(bool success)
        {
            Debug.Log($"Restore transactions completed. Success: {success}. Restored {_restoredPurchases.Count} purchases.");
            
            if (!success)
            {
                _restoredPurchases.Add(new PurchaseResult
                {
                    Status = PurchaseStatus.Failed,
                    ErrorMessage = "Restore transactions failed"
                });
            }
            
            var callback = _restorePurchasesCallback;
            _restorePurchasesCallback = null;
            callback?.Invoke(_restoredPurchases);
        }

        #endregion

        #region IStoreListener Implementation

        /// <summary>
        /// Called when Unity IAP is ready to make purchases
        /// </summary>
        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _storeController = controller;
            _extensionProvider = extensions;
            
            // Get platform-specific extensions
            _appleExtensions = extensions.GetExtension<IAppleExtensions>();
            _googlePlayExtensions = extensions.GetExtension<IGooglePlayStoreExtensions>();
            _amazonExtensions = extensions.GetExtension<IAmazonExtensions>();
            
            _isInitialized = true;
            _isInitializing = false;
            
            Debug.Log("Unity IAP initialized successfully");
            
            // Refresh our product list
            RefreshProductList();
            
            // Process any pending purchases
            ProcessPendingPurchases();
            
            // Notify listeners
            var callback = _onInitialized;
            _onInitialized = null;
            _onInitializeFailed = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Called when Unity IAP initialization fails
        /// </summary>
        public void OnInitializeFailed(InitializationFailureReason error)
        {
            OnInitializeFailed(error, null);
        }
        
        /// <summary>
        /// Called when Unity IAP initialization fails (with message)
        /// </summary>
        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            _isInitialized = false;
            _isInitializing = false;
            
            string errorMessage = $"Unity IAP initialization failed: {error}";
            if (!string.IsNullOrEmpty(message))
            {
                errorMessage += $" - {message}";
            }
            
            Debug.LogError(errorMessage);
            
            var callback = _onInitializeFailed;
            _onInitialized = null;
            _onInitializeFailed = null;
            callback?.Invoke(errorMessage);
        }

        /// <summary>
        /// Called when a purchase completes
        /// </summary>
        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            var product = args.purchasedProduct;
            
            Debug.Log($"Processing purchase: {product.definition.id}, Transaction: {product.transactionID}");
            
            // Check for pending purchase
            var pendingPurchase = _pendingPurchaseManager.GetPendingPurchaseByProductId(product.definition.id, PendingStatus.WaitingForStore);
            
            // If this is not a pending purchase, it might be a restore or direct purchase
            if (pendingPurchase == null)
            {
                Debug.LogError("failed to find Pending purchse");
                //TODO this should be fixed
                return PurchaseProcessingResult.Pending;
            }
            
            string receipt = product.receipt;
            string store = GetStoreFromReceipt(receipt);
            
            pendingPurchase.TransactionId = product.transactionID;
            pendingPurchase.Receipt = receipt;
            pendingPurchase.Store = store;
            pendingPurchase.Status = PendingStatus.ProcessingValidation;
            pendingPurchase.ErrorMessage = null;
            pendingPurchase.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            pendingPurchase.CurrencyCode = product.metadata.isoCurrencyCode;
            pendingPurchase.Price = (float)product.metadata.localizedPrice;
            _pendingPurchaseManager.SavePendingPurchases();
            
            // For restore operations, add to the list of restored purchases
            bool isRestoring = _restorePurchasesCallback != null;
            if (isRestoring)
            {
                var purchaseReceipt = new PurchaseReceipt
                {
                    ProductId = product.definition.id,
                    TransactionId = product.transactionID,
                    Receipt = receipt,
                    Store = store,
                    PurchaseTime = DateTime.Now,
                    RawReceipt = product
                };
                
                _restoredPurchases.Add(new PurchaseResult
                {
                    Status = PurchaseStatus.Success,
                    ProductId = product.definition.id,
                    Receipt = purchaseReceipt,
                    TransactionId = product.transactionID,
                    Price = (float)product.metadata.localizedPrice,
                    CurrencyCode = product.metadata.isoCurrencyCode,
                });
            }
            
            ValidatePurchaseReceipt(pendingPurchase);
            
            // If we want to control finishing the transaction ourselves, return Complete
            // Otherwise return Pending and call ConfirmPendingPurchase later
            return PurchaseProcessingResult.Pending;
        }

        /// <summary>
        /// Called when a purchase fails
        /// </summary>
        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            Debug.Log($"!!Purchase failed: {product.definition.id}");
            OnPurchaseFailed(product, failureDescription.reason);
        }
        
        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            Debug.Log($"Purchase failed: {product.definition.id}, Reason: {failureReason}");
            
            // Get the status based on the failure reason
            PurchaseStatus status;
            switch (failureReason)
            {
                case PurchaseFailureReason.UserCancelled:
                    status = PurchaseStatus.Cancelled;
                    break;
                case PurchaseFailureReason.DuplicateTransaction:
                    status = PurchaseStatus.AlreadyOwned;
                    break;
                case PurchaseFailureReason.ProductUnavailable:
                    status = PurchaseStatus.InvalidProduct;
                    break;
                default:
                    status = PurchaseStatus.Failed;
                    break;
            }

            var pendingPurchase =
                _pendingPurchaseManager.GetPendingPurchaseByProductId(product.definition.id,
                    PendingStatus.WaitingForStore);
            if (pendingPurchase != null)
            {
                ReportPaymentStatusToBalancy(pendingPurchase.ProductInfo, new PurchaseResult
                {
                    Status = status,
                    ProductId = product.definition.id,
                    ErrorMessage = failureReason.ToString()
                });

                _pendingPurchaseManager.RemovePendingPurchase(pendingPurchase);
            }
        }

        [Serializable]
        class ReceiptWrapper
        {
            public string Store;
        }

        private string GetStoreFromReceipt(string receipt)
        {
            try
            {
                // Unity receipts have a specific format with a Store field
                // First we need a class to represent this structure


                // Try to parse the receipt directly
                var receiptData = JsonUtility.FromJson<ReceiptWrapper>(receipt);
                if (receiptData != null && !string.IsNullOrEmpty(receiptData.Store))
                {
                    return receiptData.Store;
                }

                // Sometimes the receipt is a JSON string inside another JSON string
                // So we need to try a more manual approach
                if (receipt.Contains("\"Store\""))
                {
                    // Extract the Store field using string operations
                    int storeIndex = receipt.IndexOf("\"Store\"");
                    if (storeIndex >= 0)
                    {
                        int valueStart = receipt.IndexOf(":", storeIndex) + 1;
                        int valueEnd = receipt.IndexOf(",", valueStart);
                        if (valueEnd < 0) // Might be the last property
                            valueEnd = receipt.IndexOf("}", valueStart);

                        if (valueStart > 0 && valueEnd > valueStart)
                        {
                            string storeValue = receipt.Substring(valueStart, valueEnd - valueStart).Trim();
                            // Remove quotes if present
                            storeValue = storeValue.Trim('"', ' ');
                            if (!string.IsNullOrEmpty(storeValue))
                                return storeValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error parsing receipt: {ex.Message}");
            }

            return "Unknown";
        }

        #endregion
    }
}