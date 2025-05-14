// BalancyPaymentExample.cs

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Balancy.Payments.Examples
{
    /// <summary>
    /// Example showing how to use the Balancy Payment System
    /// </summary>
    public class BalancyPaymentExample : MonoBehaviour
    {
        [SerializeField] private Transform productListContainer;
        [SerializeField] private GameObject productItemPrefab;
        [SerializeField] private Button restorePurchasesButton;
        [SerializeField] private Button refreshProductsButton;
        [SerializeField] private TMP_Text statusText;
        
        private void Start()
        {
            // Set up buttons
            if (restorePurchasesButton != null)
            {
                restorePurchasesButton.onClick.AddListener(OnRestorePurchasesClick);
            }
            
            if (refreshProductsButton != null)
            {
                refreshProductsButton.onClick.AddListener(OnRefreshProductsClick);
            }
            
            // Update status
            UpdateStatus("Initializing payment system...");
            
            // Check if the payment system is already initialized
            if (BalancyPaymentManager.Instance.IsInitialized())
            {
                OnInitializeSuccess();
            }
            else
            {
                // If not, listen for initialization events
                BalancyPaymentManager.Instance.OnPurchaseCompleted += HandlePurchaseCompleted;
                
                // Update status to show we're waiting
                UpdateStatus("Initializing payment system...");
            }
        }
        
        private void HandlePurchaseCompleted(PurchaseResult result)
        {
            // Refresh our product list when purchases complete
            LoadProducts();
        }
        
        /// <summary>
        /// Called when initialization succeeds
        /// </summary>
        private void OnInitializeSuccess()
        {
            UpdateStatus("Payment system initialized successfully");
            LoadProducts();
        }
        
        /// <summary>
        /// Called when initialization fails
        /// </summary>
        private void OnInitializeFailed(string error)
        {
            UpdateStatus($"Failed to initialize payment system: {error}");
        }
        
        /// <summary>
        /// Load available products
        /// </summary>
        private void LoadProducts()
        {
            UpdateStatus("Loading products...");
            
            BalancyPaymentManager.Instance.GetProducts(products =>
            {
                UpdateStatus($"Loaded {products.Count} products");
                DisplayProducts(products);
            });
        }
        
        /// <summary>
        /// Display products in the UI
        /// </summary>
        private void DisplayProducts(List<ProductInfo> products)
        {
            // Clear existing items
            foreach (Transform child in productListContainer)
            {
                Destroy(child.gameObject);
            }
            
            // Add items for each product
            foreach (var product in products)
            {
                var item = Instantiate(productItemPrefab, productListContainer);
                var productItem = item.GetComponent<ProductItemUI>();
                
                if (productItem != null)
                {
                    productItem.Setup(product, OnBuyClicked);
                }
            }
        }
        
        /// <summary>
        /// Called when a product's buy button is clicked
        /// </summary>
        private void OnBuyClicked(ProductInfo product)
        {
            UpdateStatus($"Purchasing {product.Metadata.LocalizedTitle}...");
            
            BalancyPaymentManager.Instance.PurchaseProduct(product.ProductId, result =>
            {
                switch (result.Status)
                {
                    case PurchaseStatus.Success:
                        UpdateStatus($"Successfully purchased {product.Metadata.LocalizedTitle}!");
                        // Grant product to the player
                        GrantProduct(product);
                        break;
                    
                    case PurchaseStatus.Cancelled:
                        UpdateStatus($"Purchase cancelled by user");
                        break;
                    
                    case PurchaseStatus.Failed:
                        UpdateStatus($"Purchase failed: {result.ErrorMessage}");
                        break;
                    
                    case PurchaseStatus.Pending:
                        UpdateStatus($"Purchase is pending");
                        break;
                    
                    case PurchaseStatus.AlreadyOwned:
                        UpdateStatus($"Product already owned");
                        // Grant product to the player
                        GrantProduct(product);
                        break;
                    
                    case PurchaseStatus.InvalidProduct:
                        UpdateStatus($"Invalid product");
                        break;
                }
            });
        }
        
        /// <summary>
        /// Grant a product to the player
        /// </summary>
        private void GrantProduct(ProductInfo product)
        {
            Debug.Log($"Granting product: {product.ProductId}");
            
            // In a real game, you would grant the product to the player here
            // For consumables: give the player the item
            // For non-consumables: unlock features
            // For subscriptions: enable premium features
            
            // This is where you would make API calls to your game's systems
        }
        
        /// <summary>
        /// Called when the restore purchases button is clicked
        /// </summary>
        private void OnRestorePurchasesClick()
        {
            UpdateStatus("Restoring purchases...");
            
            BalancyPaymentManager.Instance.RestorePurchases(results =>
            {
                int successCount = 0;
                
                foreach (var result in results)
                {
                    if (result.Status == PurchaseStatus.Success)
                    {
                        successCount++;
                        
                        // Grant the product to the player
                        BalancyPaymentManager.Instance.GetProduct(result.ProductId, product =>
                        {
                            if (product != null)
                            {
                                GrantProduct(product);
                            }
                        });
                    }
                }
                
                UpdateStatus($"Restored {successCount} purchases");
            });
        }
        
        /// <summary>
        /// Called when the refresh products button is clicked
        /// </summary>
        private void OnRefreshProductsClick()
        {
            LoadProducts();
        }
        
        /// <summary>
        /// Update the status text
        /// </summary>
        private void UpdateStatus(string message)
        {
            Debug.Log($"[BalancyPaymentExample] {message}");
            
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}