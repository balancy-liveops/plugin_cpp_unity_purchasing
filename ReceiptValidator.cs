// ReceiptValidator.cs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Balancy.Payments
{
    /// <summary>
    /// Result of a receipt validation request
    /// </summary>
    [Serializable]
    public class ValidationResult
    {
        public bool IsValid;
        public string TransactionId;
        public string ProductId;
        public bool IsTestPurchase;
        public string ErrorMessage;
        public long PurchaseTime;
        public string ValidationPayload;
    }

    /// <summary>
    /// Validates purchase receipts with a server
    /// </summary>
    public class ReceiptValidator
    {
        private string _validationUrl;
        private string _validationSecret;
        private MonoBehaviour _coroutineRunner;
        
        /// <summary>
        /// Create a new receipt validator
        /// </summary>
        /// <param name="validationUrl">URL of the validation service</param>
        /// <param name="validationSecret">Secret key for validation</param>
        /// <param name="coroutineRunner">MonoBehaviour to run coroutines on</param>
        public ReceiptValidator(string validationUrl, string validationSecret, MonoBehaviour coroutineRunner)
        {
            _validationUrl = validationUrl;
            _validationSecret = validationSecret;
            _coroutineRunner = coroutineRunner;
        }

        /// <summary>
        /// Validate a purchase receipt
        /// </summary>
        /// <param name="receipt">Receipt to validate</param>
        /// <param name="callback">Callback with validation result</param>
        public void ValidateReceipt(PurchaseReceipt receipt, Action<ValidationResult> callback)
        {
            if (_coroutineRunner == null)
            {
                Debug.LogError("Cannot validate receipt: no coroutine runner provided");
                callback?.Invoke(new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "No coroutine runner provided"
                });
                return;
            }
            
            _coroutineRunner.StartCoroutine(ValidateReceiptCoroutine(receipt, callback));
        }

        /// <summary>
        /// Coroutine to validate a receipt with the server
        /// </summary>
        private IEnumerator ValidateReceiptCoroutine(PurchaseReceipt receipt, Action<ValidationResult> callback)
        {
            // Create payload
            var payload = new Dictionary<string, object>
            {
                { "productId", receipt.ProductId },
                { "transactionId", receipt.TransactionId },
                { "receipt", receipt.Receipt },
                { "store", receipt.Store },
                { "timestamp", ((DateTimeOffset)receipt.PurchaseTime).ToUnixTimeSeconds() }
            };
            
            string payloadJson = JsonUtility.ToJson(payload);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payloadJson);
            
            // Create web request
            using (var request = new UnityWebRequest(_validationUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-Validation-Secret", _validationSecret);
                
                // Send request
                yield return request.SendWebRequest();
                
                // Process response
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var result = JsonUtility.FromJson<ValidationResult>(request.downloadHandler.text);
                        callback?.Invoke(result);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error parsing validation response: {ex.Message}");
                        callback?.Invoke(new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Error parsing validation response: {ex.Message}"
                        });
                    }
                }
                else
                {
                    Debug.LogError($"Receipt validation failed: {request.error}");
                    callback?.Invoke(new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Receipt validation failed: {request.error}"
                    });
                }
            }
        }
    }
}