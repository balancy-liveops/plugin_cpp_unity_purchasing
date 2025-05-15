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
                    _instance = FindObjectOfType<BalancyPaymentManager>();
                    
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("BalancyPaymentManager");
                        go.hideFlags = HideFlags.HideAndDontSave;
                        _instance = go.AddComponent<BalancyPaymentManager>();
                        DontDestroyOnLoad(go);
                    }
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
        private Dictionary<string, Action<PurchaseResult>> _purchaseCallbacks = new Dictionary<string, Action<PurchaseResult>>();
        
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
        public event Action<PurchaseResult> OnPurchaseCompleted;
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
            Debug.LogError("SUBS");
            Balancy.Controller.OnCloudSynced -= OnCloudSynced;
            Balancy.Controller.OnCloudSynced += OnCloudSynced;
        }

        private static void OnCloudSynced()
        {
            Debug.LogError("INIT");
            Instance.Initialize();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // When resuming, check for pending purchases
            if (!pauseStatus && _isInitialized)
            {
                ProcessPendingPurchases();
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
            
            // Use provided config or fallback to serialized one
            // BalancyPaymentConfig configToUse = paymentConfig ?? config;
            //
            // if (configToUse == null)
            // {
            //     LogError("No payment configuration provided");
            //     _onInitializeFailed?.Invoke("No payment configuration provided");
            //     _onInitialized = null;
            //     _onInitializeFailed = null;
            //     return;
            // }
            
            // Create payment system
            _paymentSystem = CreatePaymentSystem();
            
            // Apply configuration
            // configToUse.ApplyConfiguration(_paymentSystem);

            if (_paymentSystem is UnityPurchaseSystem unitySystem)
                ApplyConfig(unitySystem);
            
            // Create receipt validator if needed
            // if (configToUse.ValidateReceipts && !string.IsNullOrEmpty(configToUse.ValidationServiceUrl))
            // {
            //     _receiptValidator = new ReceiptValidator(
            //         configToUse.ValidationServiceUrl,
            //         configToUse.ValidationSecret,
            //         this);
            // }
            
            // Initialize the payment system
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
        public void PurchaseProduct(string productId, Action<PurchaseResult> callback)
        {
            EnsureInitialized(() =>
            {
                Log($"Initiating purchase for product: {productId}");
                
                // Check for existing pending purchase
                var pendingPurchase = _pendingPurchaseManager.GetPendingPurchaseByProductId(productId);
                if (pendingPurchase != null && 
                    (pendingPurchase.Status == PendingStatus.WaitingForStore || 
                     pendingPurchase.Status == PendingStatus.ProcessingValidation))
                {
                    LogWarning($"Purchase already in progress for product: {productId}");
                    
                    callback?.Invoke(new PurchaseResult
                    {
                        Status = PurchaseStatus.Pending,
                        ProductId = productId,
                        ErrorMessage = "Purchase already in progress"
                    });
                    
                    return;
                }
                
                // Store callback
                _purchaseCallbacks[productId] = callback;
                
                // Initiate purchase
                _paymentSystem.PurchaseProduct(productId, result =>
                {
                    Log($"Purchase result for {productId}: {result.Status}");
                    
                    if (result.Status == PurchaseStatus.Success)
                    {
                        // Validate receipt if validator is available
                        if (_receiptValidator != null)
                        {
                            ValidateReceipt(result.Receipt, validatedResult =>
                            {
                                if (validatedResult.IsValid)
                                {
                                    // Update pending purchase status
                                    _pendingPurchaseManager.UpdatePendingPurchase(
                                        productId,
                                        result.Receipt.TransactionId,
                                        result.Receipt.Receipt,
                                        result.Receipt.Store,
                                        PendingStatus.ReadyToFinalize);
                                    
                                    // Create success result
                                    var successResult = new PurchaseResult
                                    {
                                        Status = PurchaseStatus.Success,
                                        ProductId = productId,
                                        Receipt = result.Receipt
                                    };
                                    
                                    // Complete purchase
                                    OnPurchaseComplete(productId, successResult);
                                }
                                else
                                {
                                    // Mark as failed
                                    _pendingPurchaseManager.UpdatePendingPurchase(
                                        productId,
                                        result.Receipt.TransactionId,
                                        result.Receipt.Receipt,
                                        result.Receipt.Store,
                                        PendingStatus.Failed,
                                        validatedResult.ErrorMessage);
                                    
                                    // Create failure result
                                    var failResult = new PurchaseResult
                                    {
                                        Status = PurchaseStatus.Failed,
                                        ProductId = productId,
                                        ErrorMessage = validatedResult.ErrorMessage
                                    };
                                    
                                    // Complete purchase with failure
                                    OnPurchaseComplete(productId, failResult);
                                }
                            });
                        }
                        else
                        {
                            // No validation needed, mark as ready to finalize
                            _pendingPurchaseManager.UpdatePendingPurchase(
                                productId,
                                result.Receipt.TransactionId,
                                result.Receipt.Receipt,
                                result.Receipt.Store,
                                PendingStatus.ReadyToFinalize);
                            
                            // Complete purchase
                            OnPurchaseComplete(productId, result);
                        }
                    }
                    else
                    {
                        // Update pending purchase status
                        if (pendingPurchase != null)
                        {
                            _pendingPurchaseManager.UpdatePendingPurchase(
                                productId,
                                result.Receipt?.TransactionId,
                                result.Receipt?.Receipt,
                                result.Receipt?.Store,
                                PendingStatus.Failed,
                                result.ErrorMessage);
                        }
                        
                        // Invoke callback
                        OnPurchaseComplete(productId, result);
                    }
                });
            }, error =>
            {
                callback?.Invoke(new PurchaseResult
                {
                    Status = PurchaseStatus.Failed,
                    ProductId = productId,
                    ErrorMessage = $"Payment system not initialized: {error}"
                });
            });
        }

        /// <summary>
        /// Finalize a transaction
        /// </summary>
        /// <param name="productId">Product ID</param>
        /// <param name="transactionId">Transaction ID</param>
        public void FinishTransaction(string productId, string transactionId)
        {
            if (!_isInitialized)
            {
                LogWarning("Cannot finish transaction: payment system not initialized");
                return;
            }
            
            Log($"Finishing transaction for product: {productId}, transaction: {transactionId}");
            _paymentSystem.FinishTransaction(productId, transactionId);
            _pendingPurchaseManager.RemovePendingPurchase(productId, transactionId);
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
            ProcessPendingPurchases();
            
            // Invoke callbacks
            var callback = _onInitialized;
            _onInitialized = null;
            _onInitializeFailed = null;
            callback?.Invoke();
            OnInitialized?.Invoke();
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
        /// Process any pending purchases from previous sessions
        /// </summary>
        private void ProcessPendingPurchases()
        {
            Log("Processing pending purchases...");
            
            var pendingPurchases = _pendingPurchaseManager.GetAllPendingPurchases();
            foreach (var purchase in pendingPurchases)
            {
                if (purchase.Status == PendingStatus.ReadyToFinalize)
                {
                    // This purchase was validated but not finalized
                    Log($"Found purchase ready to finalize: {purchase.ProductId}");
                    
                    // Create a purchase result for the callback
                    var result = new PurchaseResult
                    {
                        Status = PurchaseStatus.Success,
                        ProductId = purchase.ProductId,
                        Receipt = new PurchaseReceipt
                        {
                            ProductId = purchase.ProductId,
                            TransactionId = purchase.TransactionId,
                            Receipt = purchase.Receipt,
                            Store = purchase.Store,
                            PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(purchase.Timestamp).DateTime
                        }
                    };
                    
                    // Finalize the purchase
                    FinishTransaction(purchase.ProductId, purchase.TransactionId);
                    
                    // Notify listeners
                    OnPurchaseComplete(purchase.ProductId, result);
                }
                else if (purchase.Status == PendingStatus.ProcessingValidation)
                {
                    // This purchase was not validated
                    Log($"Found purchase needing validation: {purchase.ProductId}");
                    
                    if (_receiptValidator != null)
                    {
                        // Create a receipt for validation
                        var receipt = new PurchaseReceipt
                        {
                            ProductId = purchase.ProductId,
                            TransactionId = purchase.TransactionId,
                            Receipt = purchase.Receipt,
                            Store = purchase.Store,
                            PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(purchase.Timestamp).DateTime
                        };
                        
                        // Validate the receipt
                        ValidateReceipt(receipt, result =>
                        {
                            if (result.IsValid)
                            {
                                // Update status
                                _pendingPurchaseManager.UpdatePendingPurchase(
                                    purchase.ProductId,
                                    purchase.TransactionId,
                                    purchase.Receipt,
                                    purchase.Store,
                                    PendingStatus.ReadyToFinalize);
                                
                                // Create success result
                                var successResult = new PurchaseResult
                                {
                                    Status = PurchaseStatus.Success,
                                    ProductId = purchase.ProductId,
                                    Receipt = receipt
                                };
                                
                                // Finalize the purchase
                                FinishTransaction(purchase.ProductId, purchase.TransactionId);
                                
                                // Notify listeners
                                OnPurchaseComplete(purchase.ProductId, successResult);
                            }
                            else
                            {
                                // Mark as failed
                                _pendingPurchaseManager.UpdatePendingPurchase(
                                    purchase.ProductId,
                                    purchase.TransactionId,
                                    purchase.Receipt,
                                    purchase.Store,
                                    PendingStatus.Failed,
                                    result.ErrorMessage);
                                
                                // Create failure result
                                var failResult = new PurchaseResult
                                {
                                    Status = PurchaseStatus.Failed,
                                    ProductId = purchase.ProductId,
                                    ErrorMessage = result.ErrorMessage
                                };
                                
                                // Notify listeners
                                OnPurchaseComplete(purchase.ProductId, failResult);
                            }
                        });
                    }
                    else
                    {
                        // No validator, just assume it's valid
                        _pendingPurchaseManager.UpdatePendingPurchase(
                            purchase.ProductId,
                            purchase.TransactionId,
                            purchase.Receipt,
                            purchase.Store,
                            PendingStatus.ReadyToFinalize);
                        
                        // Create success result
                        var successResult = new PurchaseResult
                        {
                            Status = PurchaseStatus.Success,
                            ProductId = purchase.ProductId,
                            Receipt = new PurchaseReceipt
                            {
                                ProductId = purchase.ProductId,
                                TransactionId = purchase.TransactionId,
                                Receipt = purchase.Receipt,
                                Store = purchase.Store,
                                PurchaseTime = DateTimeOffset.FromUnixTimeSeconds(purchase.Timestamp).DateTime
                            }
                        };
                        
                        // Finalize
                        FinishTransaction(purchase.ProductId, purchase.TransactionId);
                        
                        // Notify listeners
                        OnPurchaseComplete(purchase.ProductId, successResult);
                    }
                }
            }
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
        /// Called when a purchase is complete
        /// </summary>
        private void OnPurchaseComplete(string productId, PurchaseResult result)
        {
            // Notify any registered callbacks
            if (_purchaseCallbacks.TryGetValue(productId, out var callback))
            {
                _purchaseCallbacks.Remove(productId);
                callback?.Invoke(result);
            }
            
            // Trigger event
            OnPurchaseCompleted?.Invoke(result);
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