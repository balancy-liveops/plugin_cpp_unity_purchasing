// PendingPurchaseManager.cs

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Balancy.Payments
{
    /// <summary>
    /// Status of a pending purchase
    /// </summary>
    [Serializable]
    public enum PendingStatus
    {
        WaitingForStore,       // Purchase initiated, waiting for store response
        ProcessingValidation,  // Store transaction completed, validating with server
        ReadyToFinalize,       // Purchase ready to be finalized after validation
        Failed                 // Purchase failed but kept for tracking
    }

    /// <summary>
    /// Represents a purchase that is in progress
    /// </summary>
    [Serializable]
    public class PendingPurchase
    {
        public string ProductId;
        public string TransactionId;
        public string Receipt;
        public string Store;
        public string ErrorMessage;
        public PendingStatus Status;
        public long Timestamp;

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
        public PendingPurchase AddPendingPurchase(string productId)
        {
            lock (_lock)
            {
                // Check if this product is already pending
                var existing = _data.Purchases.Find(p => p.ProductId == productId && 
                    (p.Status == PendingStatus.WaitingForStore || p.Status == PendingStatus.ProcessingValidation));
                
                if (existing != null)
                {
                    Debug.LogWarning($"Product {productId} already has a pending purchase. Returning existing.");
                    return existing;
                }

                var pendingPurchase = new PendingPurchase
                {
                    ProductId = productId,
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
                    pendingPurchase = new PendingPurchase
                    {
                        ProductId = productId,
                    };
                    _data.Purchases.Add(pendingPurchase);
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

        /// <summary>
        /// Get a pending purchase by product ID
        /// </summary>
        public PendingPurchase GetPendingPurchaseByProductId(string productId)
        {
            lock (_lock)
            {
                return _data.Purchases.Find(p => p.ProductId == productId);
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
        /// Get all purchases with a specific status
        /// </summary>
        public List<PendingPurchase> GetPendingPurchasesByStatus(PendingStatus status)
        {
            lock (_lock)
            {
                return _data.Purchases.FindAll(p => p.Status == status);
            }
        }

        /// <summary>
        /// Remove a pending purchase
        /// </summary>
        public void RemovePendingPurchase(string productId, string transactionId)
        {
            lock (_lock)
            {
                _data.Purchases.RemoveAll(p => p.ProductId == productId && p.TransactionId == transactionId);
                SavePendingPurchases();
            }
        }

        /// <summary>
        /// Clear all pending purchases
        /// </summary>
        public void ClearAllPendingPurchases()
        {
            lock (_lock)
            {
                _data.Purchases.Clear();
                SavePendingPurchases();
            }
        }

        /// <summary>
        /// Clean up old pending purchases (older than specified days)
        /// </summary>
        public void CleanupOldPendingPurchases(int olderThanDays = 7)
        {
            lock (_lock)
            {
                long cutoffTime = DateTimeOffset.UtcNow.AddDays(-olderThanDays).ToUnixTimeSeconds();
                int removedCount = _data.Purchases.RemoveAll(p => p.Timestamp < cutoffTime);
                
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
        private void SavePendingPurchases()
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