// IBalancyPaymentSystem.cs

using System;
using System.Collections.Generic;
using Balancy.Runtime.Core;

namespace Balancy.Payments
{
    /// <summary>
    /// Product type for IAP items
    /// </summary>
    public enum ProductType
    {
        Consumable = 1,
        NonConsumable = 2,
        Subscription = 3
    }

    /// <summary>
    /// Metadata about a product from the store
    /// </summary>
    [Serializable]
    public class ProductMetadata
    {
        public string LocalizedTitle { get; set; }
        public string LocalizedDescription { get; set; }
        public string LocalizedPriceString { get; set; }
        public decimal LocalizedPrice { get; set; }
        public string IsoCurrencyCode { get; set; }
    }

    /// <summary>
    /// Detailed information about a product
    /// </summary>
    [Serializable]
    public class ProductInfo
    {
        public string ProductId { get; set; }
        public string StoreSpecificId { get; set; }
        public ProductType Type { get; set; }
        public ProductMetadata Metadata { get; set; }
        public bool IsAvailable { get; set; }
        public object RawProductData { get; set; } // Store-specific product object (if needed)
    }

    /// <summary>
    /// Main interface for the payment system
    /// </summary>
    public interface IBalancyPaymentSystem
    {
        /// <summary>
        /// Initialize the payment system
        /// </summary>
        /// <param name="onInitialized">Callback when initialization completes</param>
        /// <param name="onInitializeFailed">Callback when initialization fails</param>
        void Initialize(Action onInitialized, Action<string> onInitializeFailed);

        /// <summary>
        /// Get all products information
        /// </summary>
        /// <param name="callback">Callback with the list of products</param>
        void GetProducts(Action<List<ProductInfo>> callback);

        /// <summary>
        /// Get information about a specific product
        /// </summary>
        /// <param name="productId">Product identifier</param>
        /// <param name="callback">Callback with the product information</param>
        void GetProduct(string productId, Action<ProductInfo> callback);

        /// <summary>
        /// Purchase a product
        /// </summary>
        /// <param name="productId">Product identifier</param>
        /// <param name="callback">Callback with the purchase result</param>
        void PurchaseProduct(Balancy.Actions.BalancyProductInfo productInfo);

        /// <summary>
        /// Finish a transaction (needed on some platforms)
        /// </summary>
        /// <param name="productId">Product identifier</param>
        /// <param name="transactionId">Transaction identifier</param>
        void FinishTransaction(string productId, string transactionId);

        /// <summary>
        /// Restore previously purchased products
        /// </summary>
        /// <param name="onRestoreComplete">Callback when restoration completes</param>
        void RestorePurchases(Action<List<PurchaseResult>> onRestoreComplete);

        /// <summary>
        /// Get subscription information
        /// </summary>
        /// <param name="callback">Callback with the list of subscription info</param>
        void GetSubscriptionsInfo(Action<List<SubscriptionInfo>> callback);

        /// <summary>
        /// Check if purchasing is supported on this device
        /// </summary>
        /// <returns>True if purchasing is supported</returns>
        bool IsPurchasingSupported();

        /// <summary>
        /// Check if the store setup has been completed successfully
        /// </summary>
        /// <returns>True if the store is initialized</returns>
        bool IsInitialized();

        void ProcessPendingPurchases();

        void ReportPaymentStatusToBalancy(Actions.BalancyProductInfo productInfo, PurchaseResult result);
    }
}