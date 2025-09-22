#if !NO_UNITY_PURCHASING
// UnityPurchaseSystem.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Balancy.Core;
using UnityEngine;
using UnityEngine.Purchasing;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using System.Threading.Tasks;

namespace Balancy.Payments
{
    /// <summary>
    /// Implementation of the Balancy payment system using Unity IAP v5
    /// </summary>
    public class UnityPurchaseSystem : IBalancyPaymentSystem
    {
        #region Private Fields

        private StoreController _storeController;
        
        private bool _isInitializing;
        private bool _isInitialized;
        private Action _onInitialized;
        private Action<string> _onInitializeFailed;
        
        private List<ProductInfo> _cachedProducts = new List<ProductInfo>();
        private Action<List<PurchaseResult>> _restorePurchasesCallback;
        private List<PurchaseResult> _restoredPurchases = new List<PurchaseResult>();
        
        private static UnityPurchaseSystem _instance;

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
                
                // Initialize IAP v5
                await InitializeIAPv5();
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
        
        /// <summary>
        /// Initialize IAP v5 with new async flow
        /// </summary>
        private async Task InitializeIAPv5()
        {
            try
            {
                // Get the store controller
                _storeController = UnityIAPServices.StoreController();
                
                // Set up event handlers
                _storeController.OnPurchasePending += OnPurchasePending;
                _storeController.OnPurchaseConfirmed += OnPurchaseConfirmed;
                _storeController.OnPurchaseFailed += OnPurchaseFailed;
                _storeController.OnProductsFetched += OnProductsFetched;
                _storeController.OnPurchasesFetched += OnPurchasesFetched;
                
                // Connect to the store
                await _storeController.Connect();
                
                // Fetch products if we have any
                if (_products.Count > 0)
                {
                    var productDefinitions = CreateProductDefinitions();
                    _storeController.FetchProducts(productDefinitions);
                }
                else
                {
                    // No products to fetch, consider initialization complete
                    OnInitializationComplete();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize IAP v5: {ex.Message}");
                _isInitializing = false;
                _onInitializeFailed?.Invoke(ex.Message);
                _onInitializeFailed = null;
                _onInitialized = null;
            }
        }
        
        /// <summary>
        /// Create product definitions for v5
        /// </summary>
        private List<ProductDefinition> CreateProductDefinitions()
        {
            var productDefinitions = new List<ProductDefinition>();
            
            foreach (var productInfo in _products)
            {
                string productId = productInfo.ProductId;
                string storeSpecificId = productInfo.StoreSpecificId ?? productId;
                
                UnityEngine.Purchasing.ProductType unityType;
                switch (productInfo.Type)
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

                var def = new ProductDefinition(productId, storeSpecificId, unityType);
                _productDefinitions[productId] = def;
                productDefinitions.Add(def);
            }
            
            return productDefinitions;
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
            var product = _storeController.GetProductById(productId);
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
                // Start the purchase flow using v5 API
                _storeController.PurchaseProduct(productId);
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
                // Use IAP v5 RestoreTransactions method
                Debug.Log($"Restoring purchases for {Application.platform} platform");
                _storeController.RestoreTransactions((success, error) => {
                    Debug.Log($"Restore completed. Success: {success}, Error: {error}");
                    OnRestoreTransactionsComplete(success);
                });
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
                // Note: In IAP v5, we need to access products differently
                // For now, provide basic subscription info - can be enhanced with v5 subscription APIs
                foreach (var productDef in _productDefinitions.Values)
                {
                    if (productDef.type == UnityEngine.Purchasing.ProductType.Subscription)
                    {
                        // Create basic subscription info
                        var subInfo = new SubscriptionInfo
                        {
                            ProductId = productDef.id,
                            PurchaseDate = DateTime.Now, // Should be retrieved from actual purchase data
                            ExpireDate = DateTime.Now.AddMonths(1), // Should be calculated from actual subscription
                            IsSubscribed = false, // Should be determined from actual purchase status
                            IsExpired = false,
                            IsCancelled = false,
                            IsFreeTrial = false,
                            IsAutoRenewing = true,
                            RemainingTime = TimeSpan.FromDays(30),
                            IntroductoryPrice = "",
                            IntroductoryPricePeriod = TimeSpan.Zero,
                            IntroductoryPriceCycles = 0
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
            return _storeController != null;
        }

        /// <summary>
        /// Check if the store has been initialized
        /// </summary>
        public bool IsInitialized()
        {
            return _isInitialized && _storeController != null;
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
            var paymentInfo = new PaymentInfo
            {
                ProductId = productInfo?.ProductId,
                Receipt = result.Receipt?.Receipt,
                Currency = result.CurrencyCode,
                Price = result.Price,
                OrderId = result.TransactionId
            };
            
#if UNITY_EDITOR
            bool requireValidation = false;
#else
            bool requireValidation = true;
#endif
                
            Balancy.API.FinalizedHardPurchase(ConvertStatusToResult(result.Status), productInfo, paymentInfo, (validationSuccess, removeFromPending) =>
            {
                if (validationSuccess)
                {
                    // In v5, purchase confirmation is handled by the OnPurchaseConfirmed event
                    Debug.Log("Purchase validation successful");
                    
                    _pendingPurchaseManager.RemovePendingPurchase(paymentInfo.ProductId, paymentInfo.OrderId);
                    //TODO report to apple for claiming
                }
                else
                {
                    if (removeFromPending)
                        _pendingPurchaseManager.RemovePendingPurchase(paymentInfo.ProductId, paymentInfo.OrderId);
                    else
                    {
                        //Do nothing
                        // _pendingPurchaseManager.UpdatePendingPurchaseStatus(purchaseInfo.TransactionId,
                        //     PendingStatus.ProcessingValidation);
                    }
                }
            }, requireValidation);
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
        /// Refresh the list of products (will be populated by OnProductsFetched event)
        /// </summary>
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

        #region IAP v5 Event Handlers

        /// <summary>
        /// Called when initialization completes
        /// </summary>
        private void OnInitializationComplete()
        {
            _isInitialized = true;
            _isInitializing = false;
            
            Debug.Log("Unity IAP v5 initialized successfully");
            
            // Process any pending purchases
            ProcessPendingPurchases();
            
            // Notify listeners
            var callback = _onInitialized;
            _onInitialized = null;
            _onInitializeFailed = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Called when products are successfully fetched
        /// </summary>
        private void OnProductsFetched(List<Product> products)
        {
            Debug.Log($"Products fetched successfully: {products.Count}");
            
            // Update cached products
            _cachedProducts.Clear();
            foreach (var product in products)
            {
                // Create product metadata
                var metadata = new ProductMetadata
                {
                    LocalizedTitle = product.metadata.localizedTitle,
                    LocalizedDescription = product.metadata.localizedDescription,
                    LocalizedPriceString = product.metadata.localizedPriceString,
                    LocalizedPrice = product.metadata.localizedPrice,
                    IsoCurrencyCode = product.metadata.isoCurrencyCode
                };

                // Determine product type
                ProductType productType;
                switch (product.definition.type)
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
                    ProductId = product.definition.id,
                    StoreSpecificId = product.definition.storeSpecificId,
                    Type = productType,
                    Metadata = metadata,
                    IsAvailable = product.availableToPurchase,
                    RawProductData = product
                };

                _cachedProducts.Add(productInfo);
            }
            
            // Fetch purchases after products are loaded
            _storeController.FetchPurchases();
        }

        /// <summary>
        /// Called when purchases are successfully fetched
        /// </summary>
        private void OnPurchasesFetched(Orders orders)
        {
            Debug.Log($"Purchases fetched successfully");
            
            // Process any pending orders
            foreach (var pendingOrder in orders.PendingOrders)
            {
                ProcessPendingOrder(pendingOrder);
            }
            
            // Consider initialization complete
            OnInitializationComplete();
        }

        /// <summary>
        /// Called when a purchase is pending
        /// </summary>
        private void OnPurchasePending(Order order)
        {
            // Get product info from the cart
            if (order?.CartOrdered == null || !order.CartOrdered.Items().Any())
            {
                Debug.LogWarning("OnPurchasePending called with no cart items");
                return;
            }
            
            var productId = order.CartOrdered.Items().First().Product.definition.id;
            Debug.Log($"Processing pending purchase: {productId}");
            
            if (order is PendingOrder pendingOrder)
            {
                ProcessPendingOrder(pendingOrder);
            }
        }

        /// <summary>
        /// Called when a purchase is confirmed
        /// </summary>
        private void OnPurchaseConfirmed(Order order)
        {
            // Get product info from the cart
            if (order?.CartOrdered == null || !order.CartOrdered.Items().Any())
            {
                Debug.LogWarning("OnPurchaseConfirmed called with no cart items");
                return;
            }
            
            var productId = order.CartOrdered.Items().First().Product.definition.id;
            Debug.Log($"Purchase confirmed: {productId}");
            // Purchase has been successfully confirmed
        }

        /// <summary>
        /// Process a pending order
        /// </summary>
        private void ProcessPendingOrder(PendingOrder order)
        {
            // Get product info from the cart
            if (order?.CartOrdered == null || !order.CartOrdered.Items().Any())
            {
                Debug.LogError("ProcessPendingOrder called with no cart items");
                return;
            }
            
            var cartItem = order.CartOrdered.Items().First();
            var product = cartItem.Product;
            var productId = product.definition.id;
            
            // Check for pending purchase
            var pendingPurchase = _pendingPurchaseManager.GetPendingPurchaseByProductId(productId, PendingStatus.WaitingForStore);
            
            // If this is not a pending purchase, it might be a restore or direct purchase
            if (pendingPurchase == null)
            {
                Debug.LogError($"Failed to find pending purchase for product: {productId}");
                return;
            }
            
            string receipt = order.Info.Receipt;
            string store = GetStoreFromReceipt(receipt);
            
            pendingPurchase.TransactionId = order.Info.TransactionID;
            pendingPurchase.Receipt = receipt;
            pendingPurchase.Store = store;
            pendingPurchase.Status = PendingStatus.ProcessingValidation;
            pendingPurchase.ErrorMessage = null;
            pendingPurchase.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Get currency and price from the product metadata
            pendingPurchase.CurrencyCode = product.metadata?.isoCurrencyCode ?? "USD";
            pendingPurchase.Price = (float)(product.metadata?.localizedPrice ?? 0.0m);
            _pendingPurchaseManager.SavePendingPurchases();
            
            // For restore operations, add to the list of restored purchases
            bool isRestoring = _restorePurchasesCallback != null;
            if (isRestoring)
            {
                var purchaseReceipt = new PurchaseReceipt
                {
                    ProductId = productId,
                    TransactionId = order.Info.TransactionID,
                    Receipt = receipt,
                    Store = store,
                    PurchaseTime = DateTime.Now
                };
                
                _restoredPurchases.Add(new PurchaseResult
                {
                    Status = PurchaseStatus.Success,
                    ProductId = productId,
                    Receipt = purchaseReceipt,
                    TransactionId = order.Info.TransactionID,
                    Price = (float)(product?.metadata?.localizedPrice ?? 0.0m),
                    CurrencyCode = product?.metadata?.isoCurrencyCode ?? "USD",
                });
            }
            
            ValidatePurchaseReceipt(pendingPurchase);
            
            // Confirm the purchase
            _storeController.ConfirmPurchase(order);
        }

        /// <summary>
        /// Called when a purchase fails
        /// </summary>
        private void OnPurchaseFailed(FailedOrder failedOrder)
        {
            // Get product info from the cart
            string productId = "unknown";
            if (failedOrder?.CartOrdered != null && failedOrder.CartOrdered.Items().Any())
            {
                productId = failedOrder.CartOrdered.Items().First().Product.definition.id;
            }
            
            Debug.Log($"Purchase failed: {productId}, Reason: {failedOrder.FailureReason}");
            
            // Get the status based on the failure reason
            PurchaseStatus status;
            switch (failedOrder.FailureReason)
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

            var pendingPurchase = _pendingPurchaseManager.GetPendingPurchaseByProductId(
                productId, PendingStatus.WaitingForStore);
            
            if (pendingPurchase != null)
            {
                ReportPaymentStatusToBalancy(pendingPurchase.ProductInfo, new PurchaseResult
                {
                    Status = status,
                    ProductId = productId,
                    ErrorMessage = failedOrder.Details
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
#endif