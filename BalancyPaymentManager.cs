// BalancyPaymentManager.cs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Balancy.Payments
{
    /// <summary>
    /// Main manager class for the Balancy payment system
    /// </summary>
    public class BalancyPaymentManager : MonoBehaviour
    {
        #region Singleton

        private static BalancyPaymentManager _instance;
        
        /// <summary>
        /// Get the singleton instance
        /// </summary>
        private static BalancyPaymentManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("BalancyPaymentManager");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    _instance = go.AddComponent<BalancyPaymentManager>();
                    DontDestroyOnLoad(go);
                }
                
                return _instance;
            }
        }

        #endregion
        
        #region Inspector Fields

        [SerializeField] private BalancyPaymentConfig config;
        // [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool debugMode = true;

        #endregion
        
        #region Private Fields

        private IBalancyPaymentSystem _paymentSystem;
        private ReceiptValidator _receiptValidator;
        private bool _isInitialized;
        private PendingPurchaseManager _pendingPurchaseManager => PendingPurchaseManager.Instance;
        private Action _onInitialized;
        private Action<string> _onInitializeFailed;
        
        // Cache of products
        private Dictionary<string, ProductInfo> _productCache = new Dictionary<string, ProductInfo>();
        
        // Track purchases that are waiting for validation
        private Dictionary<string, PurchasePendingValidation> _validationQueue = new Dictionary<string, PurchasePendingValidation>();
        
        private class PurchasePendingValidation
        {
            public PurchaseReceipt Receipt;
            public Action<PurchaseResult> Callback;
        }

        #endregion

        #region Events

        /// <summary>
        /// Event fired when a purchase is completed
        /// </summary>
        public event Action OnInitialized;

        /// <summary>
        /// Event fired when purchases are restored
        /// </summary>
        public event Action<List<PurchaseResult>> OnPurchasesRestored;

        #endregion
        
        #region Unity Lifecycle

        [RuntimeInitializeOnLoadMethod]
        private static void Init()
        {
            Balancy.Controller.OnCloudSynced -= OnCloudSynced;
            Balancy.Controller.OnCloudSynced += OnCloudSynced;
        }

        private static void OnCloudSynced()
        {
            Instance.Initialize();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // When resuming, check for pending purchases
            if (!pauseStatus && _isInitialized && _paymentSystem != null)
            {
                _paymentSystem.ProcessPendingPurchases();
            }
        }

        #endregion
        
        #region Public Methods

        /// <summary>
        /// Initialize the payment system with a configuration
        /// </summary>
        /// <param name="paymentConfig">Configuration to use</param>
        /// <param name="onInitialized">Callback when initialized</param>
        /// <param name="onInitializeFailed">Callback when initialization fails</param>
        private void Initialize(BalancyPaymentConfig paymentConfig = null, Action onInitialized = null, Action<string> onInitializeFailed = null)
        {
            if (_isInitialized)
            {
                onInitialized?.Invoke();
                return;
            }
            
            // Save callbacks
            if (onInitialized != null)
            {
                _onInitialized += onInitialized;
            }
            
            if (onInitializeFailed != null)
            {
                _onInitializeFailed += onInitializeFailed;
            }

            _paymentSystem = CreatePaymentSystem();
            
            if (_paymentSystem is UnityPurchaseSystem unitySystem)
                ApplyConfig(unitySystem);

            _paymentSystem.Initialize(OnPaymentSystemInitialized, OnPaymentSystemInitializeFailed);
        }

        private void ApplyConfig(UnityPurchaseSystem unitySystem)
        {
            var productsAndTypes = Balancy.API.GetProductsIdAndType();
            //TODO - implement this
            // unitySystem.AutoFinishTransactions = AutoFinishTransactions;
            unitySystem.UnityEnvironment = "production";
            
            for (int i = 0; i < productsAndTypes.Length; i += 2)
            {
                var id = productsAndTypes[i];
                if (int.TryParse(productsAndTypes[i + 1], out var type))
                {
                    unitySystem.AddProduct(id, (ProductType)type);
                }
                else
                {
                    Debug.LogError("Failed to parse type " + id + " : " + productsAndTypes[i + 1]);
                }
            }
        }

        /// <summary>
        /// Get all products
        /// </summary>
        /// <param name="callback">Callback with product list</param>
        /// <param name="forceRefresh">Whether to force a refresh of the cache</param>
        public void GetProducts(Action<List<ProductInfo>> callback, bool forceRefresh = false)
        {
            EnsureInitialized(() =>
            {
                // Use cache if available and not forcing refresh
                if (!forceRefresh && _productCache.Count > 0)
                {
                    callback?.Invoke(new List<ProductInfo>(_productCache.Values));
                    return;
                }
                
                _paymentSystem.GetProducts(products =>
                {
                    // Update cache
                    _productCache.Clear();
                    foreach (var product in products)
                    {
                        _productCache[product.ProductId] = product;
                    }
                    
                    callback?.Invoke(products);
                });
            }, error => callback?.Invoke(new List<ProductInfo>()));
        }

        /// <summary>
        /// Get a specific product
        /// </summary>
        /// <param name="productId">Product ID</param>
        /// <param name="callback">Callback with product info</param>
        /// <param name="forceRefresh">Whether to force a refresh of the cache</param>
        public void GetProduct(string productId, Action<ProductInfo> callback, bool forceRefresh = false)
        {
            EnsureInitialized(() =>
            {
                // Use cache if available and not forcing refresh
                if (!forceRefresh && _productCache.TryGetValue(productId, out var cachedProduct))
                {
                    callback?.Invoke(cachedProduct);
                    return;
                }
                
                _paymentSystem.GetProduct(productId, product =>
                {
                    // Update cache
                    if (product != null)
                    {
                        _productCache[productId] = product;
                    }
                    
                    callback?.Invoke(product);
                });
            }, error => callback?.Invoke(null));
        }

        /// <summary>
        /// Purchase a product
        /// </summary>
        /// <param name="productId">Product ID</param>
        /// <param name="callback">Callback with purchase result</param>
        private void PurchaseProduct(Balancy.Actions.BalancyProductInfo productInfo)
        {
            EnsureInitialized(() =>
            {
                var productId = productInfo.ProductId;
                Log($"Initiating purchase for product: {productId}");
                
                // Check for existing pending purchase
                // var pendingPurchase = _pendingPurchaseManager.GetPendingPurchaseByProductId(productId);
                // if (pendingPurchase != null && 
                //     (pendingPurchase.Status == PendingStatus.WaitingForStore || 
                //      pendingPurchase.Status == PendingStatus.ProcessingValidation))
                // {
                //     LogWarning($"Purchase already in progress for product: {productId}");
                //     
                //     _paymentSystem.ReportPaymentStatusToBalancy(productInfo, new PurchaseResult
                //     {
                //         Status = PurchaseStatus.Pending,
                //         ProductId = productId,
                //         ErrorMessage = "Purchase already in progress"
                //     });
                //     return;
                // }
                
                _paymentSystem.PurchaseProduct(productInfo);
            }, error =>
            {
                _paymentSystem.ReportPaymentStatusToBalancy(productInfo, new PurchaseResult
                {
                    Status = PurchaseStatus.Failed,
                    ProductId = productInfo.ProductId,
                    ErrorMessage = $"Payment system not initialized: {error}"
                });
            });
        }

        /// <summary>
        /// Restore previously purchased products
        /// </summary>
        /// <param name="callback">Callback with restored purchases</param>
        public void RestorePurchases(Action<List<PurchaseResult>> callback)
        {
            EnsureInitialized(() =>
            {
                Log("Restoring purchases...");
                
                _paymentSystem.RestorePurchases(results =>
                {
                    Log($"Restored {results.Count} purchases");
                    
                    // Validate each restored purchase if validator is available
                    if (_receiptValidator != null && results.Count > 0)
                    {
                        var validatedResults = new List<PurchaseResult>();
                        var validationCount = 0;
                        
                        foreach (var result in results)
                        {
                            if (result.Status == PurchaseStatus.Success && result.Receipt != null)
                            {
                                ValidateReceipt(result.Receipt, validatedResult =>
                                {
                                    if (validatedResult.IsValid)
                                    {
                                        validatedResults.Add(result);
                                    }
                                    else
                                    {
                                        // Add result with failure status
                                        validatedResults.Add(new PurchaseResult
                                        {
                                            Status = PurchaseStatus.Failed,
                                            ProductId = result.ProductId,
                                            ErrorMessage = validatedResult.ErrorMessage
                                        });
                                    }
                                    
                                    validationCount++;
                                    
                                    if (validationCount == results.Count)
                                    {
                                        // All validations completed
                                        OnPurchasesRestored?.Invoke(validatedResults);
                                        callback?.Invoke(validatedResults);
                                    }
                                });
                            }
                            else
                            {
                                validatedResults.Add(result);
                                validationCount++;
                                
                                if (validationCount == results.Count)
                                {
                                    // All validations completed
                                    OnPurchasesRestored?.Invoke(validatedResults);
                                    callback?.Invoke(validatedResults);
                                }
                            }
                        }
                    }
                    else
                    {
                        // No validation needed
                        OnPurchasesRestored?.Invoke(results);
                        callback?.Invoke(results);
                    }
                });
            }, error => callback?.Invoke(new List<PurchaseResult>()));
        }

        /// <summary>
        /// Get subscription information
        /// </summary>
        /// <param name="callback">Callback with subscription info</param>
        public void GetSubscriptionsInfo(Action<List<SubscriptionInfo>> callback)
        {
            EnsureInitialized(() =>
            {
                _paymentSystem.GetSubscriptionsInfo(callback);
            }, error => callback?.Invoke(new List<SubscriptionInfo>()));
        }

        /// <summary>
        /// Check if the payment system is initialized
        /// </summary>
        /// <returns>True if initialized</returns>
        public bool IsInitialized()
        {
            return _isInitialized && _paymentSystem != null && _paymentSystem.IsInitialized();
        }

        /// <summary>
        /// Check if purchasing is supported on this device
        /// </summary>
        /// <returns>True if supported</returns>
        public bool IsPurchasingSupported()
        {
            return _isInitialized && _paymentSystem != null && _paymentSystem.IsPurchasingSupported();
        }

        #endregion
        
        #region Private Methods

        /// <summary>
        /// Create the appropriate payment system based on platform
        /// </summary>
        private IBalancyPaymentSystem CreatePaymentSystem()
        {
            // For now, we just have Unity's system
            return UnityPurchaseSystem.Instance;
        }

        /// <summary>
        /// Called when the payment system is initialized
        /// </summary>
        private void OnPaymentSystemInitialized()
        {
            Log("Payment system initialized successfully");
            
            _isInitialized = true;
            
            // Process any pending purchases
            var callback = _onInitialized;
            _onInitialized = null;
            _onInitializeFailed = null;
            callback?.Invoke();
            OnInitialized?.Invoke();
            
            Balancy.Actions.Purchasing.SetHardPurchaseCallback(TryToHardPurchase);
            
            Balancy.Callbacks.OnPaymentIsReady?.Invoke();
        }

        private void TryToHardPurchase(Balancy.Actions.BalancyProductInfo productInfo)
        {
            var productId = productInfo?.ProductId;
            
            if (string.IsNullOrEmpty(productId))
            {
                Debug.LogError("Product ID is null or empty");
                Balancy.API.FinalizedHardPurchase(Actions.PurchaseResult.Failed, productInfo, new Actions.PurchaseInfo
                {
                    ErrorMessage = "Product ID is null or empty"
                }, null);
                return;
            }

            PurchaseProduct(productInfo);
        }

        /// <summary>
        /// Called when payment system initialization fails
        /// </summary>
        private void OnPaymentSystemInitializeFailed(string error)
        {
            LogError($"Payment system initialization failed: {error}");
            
            _isInitialized = false;
            
            // Invoke callbacks
            var callback = _onInitializeFailed;
            _onInitialized = null;
            _onInitializeFailed = null;
            callback?.Invoke(error);
        }
        
        /// <summary>
        /// Validate a receipt with the server
        /// </summary>
        private void ValidateReceipt(PurchaseReceipt receipt, Action<ValidationResult> callback)
        {
            if (_receiptValidator == null)
            {
                callback?.Invoke(new ValidationResult { IsValid = true });
                return;
            }
            
            Log($"Validating receipt for product: {receipt.ProductId}");
            
            _receiptValidator.ValidateReceipt(receipt, callback);
        }

        /// <summary>
        /// Ensure the payment system is initialized
        /// </summary>
        private void EnsureInitialized(Action onInitialized, Action<string> onFailed)
        {
            if (IsInitialized())
            {
                onInitialized?.Invoke();
            }
            else
            {
                // Initialize with default config
                Initialize(
                    config, 
                    onInitialized, 
                    onFailed);
            }
        }

        #endregion
        
        #region Logging

        private void Log(string message)
        {
            if (debugMode)
            {
                Debug.Log($"[BalancyPayments] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (debugMode)
            {
                Debug.LogWarning($"[BalancyPayments] {message}");
            }
        }

        private void LogError(string message)
        {
            if (debugMode)
            {
                Debug.LogError($"[BalancyPayments] {message}");
            }
        }

        #endregion
    }
}