// BalancyPaymentConfig.cs

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Balancy.Payments
{
    /// <summary>
    /// Configuration class for Balancy Payments
    /// </summary>
    [CreateAssetMenu(fileName = "BalancyPaymentConfig", menuName = "Balancy/Payment Config")]
    public class BalancyPaymentConfig : ScriptableObject
    {
        [Serializable]
        public class ProductDefinition
        {
            public string ProductId;
            public ProductType Type;
            [Tooltip("Leave empty to use ProductId as the store-specific ID")]
            public string StoreSpecificId;
        }

        #region Inspector Fields

        [Header("Products")]
        [SerializeField] private List<ProductDefinition> products = new List<ProductDefinition>();

        [Header("Settings")]
        [SerializeField] private bool autoInitializeOnStart = true;
        [SerializeField] private bool autoFinishTransactions = true;
        [SerializeField] private string unityEnvironment = "production";
        [SerializeField] private bool enableLogging = true;

        [Header("Validation")]
        [SerializeField] private bool validateReceipts = true;
        [SerializeField] private string validationServiceUrl;
        [SerializeField] private string validationSecret;

        #endregion

        #region Properties

        public List<ProductDefinition> Products => products;
        public bool AutoInitializeOnStart => autoInitializeOnStart;
        public bool AutoFinishTransactions => autoFinishTransactions;
        public string UnityEnvironment => unityEnvironment;
        public bool EnableLogging => enableLogging;
        public bool ValidateReceipts => validateReceipts;
        public string ValidationServiceUrl => validationServiceUrl;
        public string ValidationSecret => validationSecret;

        #endregion

        /// <summary>
        /// Apply this configuration to the payment system
        /// </summary>
        /// <param name="paymentSystem">Payment system to configure</param>
        public void ApplyConfiguration(IBalancyPaymentSystem paymentSystem)
        {
            if (paymentSystem is UnityPurchaseSystem unitySystem)
            {
                unitySystem.AutoFinishTransactions = AutoFinishTransactions;
                unitySystem.UnityEnvironment = UnityEnvironment;

                // Add product definitions
                foreach (var product in Products)
                {
                    unitySystem.AddProduct(product.ProductId, product.Type, product.StoreSpecificId);
                }
            }
        }
    }
}