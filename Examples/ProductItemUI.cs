using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Balancy.Payments.Examples
{
    /// <summary>
    /// UI component for displaying a product
    /// </summary>
    public class ProductItemUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private Button buyButton;
        
        private ProductInfo _product;
        private Action<ProductInfo> _onBuyClicked;
        
        /// <summary>
        /// Set up the product item
        /// </summary>
        public void Setup(ProductInfo product, Action<ProductInfo> onBuyClicked)
        {
            _product = product;
            _onBuyClicked = onBuyClicked;
            
            // Set up UI
            if (titleText != null)
            {
                titleText.text = product.Metadata.LocalizedTitle;
            }
            
            if (descriptionText != null)
            {
                descriptionText.text = product.Metadata.LocalizedDescription;
            }
            
            if (priceText != null)
            {
                priceText.text = product.Metadata.LocalizedPriceString;
            }
            
            if (buyButton != null)
            {
                buyButton.onClick.AddListener(OnBuyClicked);
                buyButton.interactable = product.IsAvailable;
            }
        }
        
        /// <summary>
        /// Called when the buy button is clicked
        /// </summary>
        private void OnBuyClicked()
        {
            _onBuyClicked?.Invoke(_product);
        }
        
        private void OnDestroy()
        {
            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(OnBuyClicked);
            }
        }
    }
}
