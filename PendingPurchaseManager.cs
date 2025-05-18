// PendingPurchaseManager.cs

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Balancy.Payments
{
    /// <summary>
    /// Status of a pending purchase
    /// </summary>
    [Serializable]
    public enum PendingStatus
    {
        WaitingForStore = 0,       // Purchase initiated, waiting for store response
        ProcessingValidation = 1,  // Store transaction completed, validating with server
        Failed = 3             // Purchase failed but kept for tracking
    }

    /// <summary>
    /// Represents a purchase that is in progress
    /// </summary>
    [Serializable]
    public class PendingPurchase
    {
        public string TransactionId;
        public string Receipt;
        public string Store;
        public string ErrorMessage;
        public PendingStatus Status;
        public long Timestamp;

        public Balancy.Actions.BalancyProductInfo ProductInfo;
        
        public bool Equals(Balancy.Actions.BalancyProductInfo productInfo)
        {
            return ProductInfo.ProductId == productInfo.ProductId && ProductInfo.Equals(productInfo);
        }

        public string CurrencyCode;
        public float Price;

        public PendingPurchase()
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    /// <summary>
    /// Container for serializing pending purchases
    /// </summary>
    [Serializable]
    public class PendingPurchasesData
    {
        public List<PendingPurchase> Purchases = new List<PendingPurchase>();
    }

    /// <summary>
    /// Manages pending purchases to handle edge cases like app crashes during purchase
    /// </summary>
    public class PendingPurchaseManager
    {
        private const string PENDING_PURCHASES_FILE = "balancy_pending_purchases.json";
        private static PendingPurchaseManager _instance;
        private PendingPurchasesData _data;
        private object _lock = new object();

        public static PendingPurchaseManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new PendingPurchaseManager();
                return _instance;
            }
        }

        private PendingPurchaseManager()
        {
            LoadPendingPurchases();
        }

        /// <summary>
        /// Add a new pending purchase
        /// </summary>
        public PendingPurchase AddPendingPurchase(Balancy.Actions.BalancyProductInfo productInfo)
        {
            lock (_lock)
            {
                // Check if this product is already pending
                // var existing = _data.Purchases.Find(p => p.Equals(productInfo) && 
                //     (p.Status == PendingStatus.WaitingForStore || p.Status == PendingStatus.ProcessingValidation));
                //
                // if (existing != null)
                // {
                //     Debug.LogWarning($"Product {productInfo.ProductId} already has a pending purchase. Returning existing.");
                //     return existing;
                // }

                var pendingPurchase = new PendingPurchase
                {
                    ProductInfo = productInfo,
                    Status = PendingStatus.WaitingForStore
                };

                _data.Purchases.Add(pendingPurchase);
                SavePendingPurchases();
                return pendingPurchase;
            }
        }

        /// <summary>
        /// Update a pending purchase's status
        /// </summary>
        public void UpdatePendingPurchase(string productId, string transactionId, string receipt, 
            string store, PendingStatus status, string errorMessage = null)
        {
            lock (_lock)
            {
                var pendingPurchase = GetPendingPurchaseByProductId(productId);
                
                if (pendingPurchase == null)
                {
                    Debug.LogError("something went wrong, no pending purchase found: " + productId);
                    return;
                }

                pendingPurchase.TransactionId = transactionId;
                pendingPurchase.Receipt = receipt;
                pendingPurchase.Store = store;
                pendingPurchase.Status = status;
                pendingPurchase.ErrorMessage = errorMessage;
                pendingPurchase.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                SavePendingPurchases();
            }
        }
        
        public void UpdatePendingPurchasePrice(string productId, string currencyCode, decimal localizedPrice)
        {
            lock (_lock)
            {
                var pendingPurchase = GetPendingPurchaseByProductId(productId);
                
                if (pendingPurchase == null)
                {
                    Debug.LogError(">something went wrong, no pending purchase found: " + productId);
                    return;
                }

                pendingPurchase.CurrencyCode = currencyCode;
                pendingPurchase.Price = (float)localizedPrice;

                SavePendingPurchases();
            }
        }
        
        public void UpdatePendingPurchaseStatus(string transactionId, PendingStatus status)
        {
            lock (_lock)
            {
                var pendingPurchase = GetPendingPurchaseByTransactionId(transactionId);
                
                if (pendingPurchase == null)
                {
                    Debug.LogError("something went wrong, no pending purchase found: " + transactionId);
                    return;
                }

                pendingPurchase.Status = status;
                pendingPurchase.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                SavePendingPurchases();
            }
        }

        /// <summary>
        /// Get a pending purchase by product ID
        /// </summary>
        public PendingPurchase GetPendingPurchaseByProductId(string productId)
        {
            lock (_lock)
            {
                return _data.Purchases.Find(p => p.ProductInfo.ProductId == productId);
            }
        }
        
        public PendingPurchase GetPendingPurchaseByProductId(string productId, PendingStatus status)
        {
            lock (_lock)
            {
                return _data.Purchases.Find(p => p.ProductInfo.ProductId == productId && p.Status == status);
            }
        }

        /// <summary>
        /// Get a pending purchase by transaction ID
        /// </summary>
        public PendingPurchase GetPendingPurchaseByTransactionId(string transactionId)
        {
            lock (_lock)
            {
                return _data.Purchases.Find(p => p.TransactionId == transactionId);
            }
        }

        /// <summary>
        /// Get all pending purchases
        /// </summary>
        public List<PendingPurchase> GetAllPendingPurchases()
        {
            lock (_lock)
            {
                return new List<PendingPurchase>(_data.Purchases);
            }
        }

        /// <summary>
        /// Remove a pending purchase
        /// </summary>
        public void RemovePendingPurchase(string productId, string transactionId)
        {
            lock (_lock)
            {
                _data.Purchases.RemoveAll(p => p.ProductInfo.ProductId == productId && p.TransactionId == transactionId);
                SavePendingPurchases();
            }
        }

        public void RemovePendingPurchase(PendingPurchase pendingPurchase)
        {
            lock (_lock)
            {
                _data.Purchases.Remove(pendingPurchase);
                SavePendingPurchases();
            }
        }
        
        /// <summary>
        /// Clean up old pending purchases (older than specified days)
        /// </summary>
        public void CleanupOldPendingPurchases(int olderThanDays = 30)
        {
            lock (_lock)
            {
                long cutoffTime = DateTimeOffset.UtcNow.AddDays(-olderThanDays).ToUnixTimeSeconds();
                int removedCount = _data.Purchases.RemoveAll(p => p.Timestamp < cutoffTime || p.Status == PendingStatus.WaitingForStore);
                
                if (removedCount > 0)
                {
                    Debug.Log($"Cleaned up {removedCount} old pending purchases.");
                    SavePendingPurchases();
                }
            }
        }

        /// <summary>
        /// Load pending purchases from disk
        /// </summary>
        private void LoadPendingPurchases()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, PENDING_PURCHASES_FILE);
                
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _data = JsonUtility.FromJson<PendingPurchasesData>(json) ?? new PendingPurchasesData();
                    
                    Debug.Log($"Loaded {_data.Purchases.Count} pending purchases.");
                    
                    // Clean up old entries
                    CleanupOldPendingPurchases();
                }
                else
                {
                    _data = new PendingPurchasesData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading pending purchases: {ex.Message}");
                _data = new PendingPurchasesData();
            }
        }

        /// <summary>
        /// Save pending purchases to disk
        /// </summary>
        public void SavePendingPurchases()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, PENDING_PURCHASES_FILE);
                string json = JsonUtility.ToJson(_data, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving pending purchases: {ex.Message}");
            }
        }
    }
}