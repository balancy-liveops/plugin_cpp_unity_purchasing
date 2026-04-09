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
        private static readonly WaitForFixedUpdate FixedUpdate = new WaitForFixedUpdate();
        
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

        public static void SetPaymentSystem(IBalancyPaymentSystem system)
        {
            Instance._paymentSystem = system;
        }

        #endregion
        
        #region Inspector Fields

        // [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool debugMode = true;

        #endregion
        
        #region Private Fields

        private IBalancyPaymentSystem _paymentSystem;
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
        public static event Action<List<PurchaseResult>> OnPurchasesRestored;

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


        #endregion
        
        #region Public Methods

        /// <summary>
        /// Initialize the payment system with a configuration
        /// </summary>
        /// <param name="paymentConfig">Configuration to use</param>
        /// <param name="onInitialized">Callback when initialized</param>
        /// <param name="onInitializeFailed">Callback when initialization fails</param>
        private void Initialize(Action onInitialized = null, Action<string> onInitializeFailed = null)
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

            InitPaymentSystem();
        }

        private void InitPaymentSystem()
        {
            CreatePaymentSystem(() =>
            {
                Debug.LogWarning($"[BalancyPayments] _paymentSystem ready: {_paymentSystem.GetType().Name}");
                FetchProductsAndInitialize();
            });
        }

        private void FetchProductsAndInitialize()
        {
            Debug.LogWarning("[BalancyPayments] Fetching products via Balancy.API.GetProducts(...)");
            Balancy.API.GetProducts(response =>
            {
                if (response == null || !response.Success || response.Products == null || response.Products.Count == 0)
                {
                    var errorMessage = response == null
                        ? "null response"
                        : $"success={response.Success}, products={(response.Products == null ? 0 : response.Products.Count)}, error={response.ErrorMessage}";
                    LogError($"GetProducts returned no usable data ({errorMessage}). Skipping StoreKit init — payment system will retry on next OnCloudSynced.");
                    OnPaymentSystemInitializeFailed("No products available");
                    return;
                }

                Debug.LogWarning($"[BalancyPayments] GetProducts returned {response.Products.Count} products");

#if !NO_UNITY_PURCHASING
                if (_paymentSystem is UnityPurchaseSystem unitySystem)
                    ApplyConfig(unitySystem, response.Products);
#endif

                Debug.LogWarning("[BalancyPayments] Calling _paymentSystem.Initialize(...)");
                _paymentSystem.Initialize(OnPaymentSystemInitialized, OnPaymentSystemInitializeFailed);
            });
        }
#if !NO_UNITY_PURCHASING
        private void ApplyConfig(UnityPurchaseSystem unitySystem, List<Balancy.Core.Responses.Product> products)
        {
            //TODO - implement this
            // unitySystem.AutoFinishTransactions = AutoFinishTransactions;
            unitySystem.UnityEnvironment = "production";

            foreach (var product in products)
            {
                var id = product.PlatformProductId;
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogError($"[BalancyPayments] ApplyConfig: skipping product '{product.ProductId}' — empty PlatformProductId");
                    continue;
                }

                var type = ConvertProductType(product.Type);
                unitySystem.AddProduct(id, type);
            }
        }

        private static ProductType ConvertProductType(Balancy.Core.Responses.ProductType coreType)
        {
            switch (coreType)
            {
                case Balancy.Core.Responses.ProductType.Consumable:
                    return ProductType.Consumable;
                case Balancy.Core.Responses.ProductType.NonConsumable:
                    return ProductType.NonConsumable;
                case Balancy.Core.Responses.ProductType.Subscription:
                    return ProductType.Subscription;
                default:
                    return ProductType.Consumable;
            }
        }
#endif
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

        private void RestorePurchases()
        {
            RestorePurchases(null);
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
                    
                    for (int i = 0; i < results.Count; i++)
                    {
                        var result = results[i];
                        Log($"Restored purchase: {result.ProductId}, Status: {result.Status}");
                        
                        // Validate receipt if needed
                        // if (result.Status == PurchaseStatus.Restored)
                        // {
                        //     ValidateReceipt(result.Receipt, validationResult =>
                        //     {
                        //         if (validationResult.IsValid)
                        //         {
                        //             result.Status = PurchaseStatus.Validated;
                        //             result.ErrorMessage = null;
                        //         }
                        //         else
                        //         {
                        //             result.Status = PurchaseStatus.Invalid;
                        //             result.ErrorMessage = validationResult.ErrorMessage;
                        //         }
                        //         
                        //         // Invoke callback with the updated result
                        //         callback?.Invoke(results);
                        //     });
                        // }
                    }
                    
                    // Fire the event for external listeners
                    OnPurchasesRestored?.Invoke(results);
                    
                    // Call the provided callback
                    callback?.Invoke(results);
                    
                    // Validate each restored purchase if validator is available
                   
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
        private void CreatePaymentSystem(Action inited)
        {
            WaitUntil(() => this._paymentSystem != null, inited);
        }
        
        internal Coroutine WaitUntil(Func<bool> condition, Action callback)
        {
            return StartCoroutine(WaitUntilInternal(condition, callback));
        }
        
        private static IEnumerator WaitUntilInternal(Func<bool> condition, Action callback)
        {
            while (!condition())
            {
                yield return FixedUpdate;
            }

            callback?.Invoke();
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
            Balancy.Actions.Purchasing.SetRestorePurchasesCallback(RestorePurchases);
            
            Balancy.Callbacks.SetPaymentIsReady();
        }

        private void TryToHardPurchase(Balancy.Actions.BalancyProductInfo productInfo)
        {
            var productId = productInfo?.ProductId;
            
            if (string.IsNullOrEmpty(productId))
            {
                Balancy.API.FinalizedHardPurchase(Actions.PurchaseResult.Failed, productInfo, null, null);
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