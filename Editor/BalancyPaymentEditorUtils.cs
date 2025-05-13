// BalancyPaymentEditorUtils.cs

using UnityEngine;
using UnityEditor;
using System.IO;

namespace Balancy.Payments.Editor
{
    /// <summary>
    /// Editor utilities for the Balancy Payment system
    /// </summary>
    public static class BalancyPaymentEditorUtils
    {
        [MenuItem("Balancy/Payments/Create Default Configuration")]
        public static void CreateDefaultConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Payment Configuration",
                "BalancyPaymentConfig",
                "asset",
                "Save the payment configuration asset");
            
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            
            // Create the configuration asset
            var config = ScriptableObject.CreateInstance<BalancyPaymentConfig>();
            
            // Save it to disk
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            
            // Ping the asset to show it in the Project view
            EditorGUIUtility.PingObject(config);
            
            Debug.Log($"Created payment configuration at {path}");
        }
        
        [MenuItem("Balancy/Payments/Setup Project for IAP")]
        public static void SetupProjectForIAP()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Setup IAP",
                "This will add package dependencies for Unity IAP and setup your project for in-app purchases. Continue?",
                "Yes",
                "No");
            
            if (!confirmed)
            {
                return;
            }
            
            // Add Unity IAP package
            UnityEditor.PackageManager.Client.Add("com.unity.purchasing@4.12.2");
            
            Debug.Log("Added Unity IAP package. Please wait for the package manager to complete installation.");
            
            // We could also add scripting define symbols, etc. here if needed
        }

        [MenuItem("Balancy/Payments/Documentation")]
        public static void OpenDocumentation()
        {
            // Create documentation if it doesn't exist
            string docsFolder = Path.Combine(Application.dataPath, "BalancyPayments/Documentation");
            string docsFile = Path.Combine(docsFolder, "README.md");
            
            if (!Directory.Exists(docsFolder))
            {
                Directory.CreateDirectory(docsFolder);
            }
            
            if (!File.Exists(docsFile))
            {
                string content = "# Balancy Payments Documentation\n\n" +
                                "This documentation covers how to integrate and use the Balancy Payments system.\n\n" +
                                "## Getting Started\n\n" +
                                "1. Create a payment configuration asset (Balancy > Payments > Create Default Configuration)\n" +
                                "2. Add products to the configuration\n" +
                                "3. Add the BalancyPaymentManager component to a GameObject in your scene\n" +
                                "4. Assign the configuration to the manager\n\n" +
                                "## Integration with Unity IAP\n\n" +
                                "The Balancy Payment system uses Unity IAP 4.12.2. Make sure this package is installed in your project.\n\n" +
                                "## API Usage\n\n" +
                                "```csharp\n" +
                                "// Initialize the payment system\n" +
                                "BalancyPaymentManager.Instance.Initialize();\n\n" +
                                "// Get available products\n" +
                                "BalancyPaymentManager.Instance.GetProducts(products => {\n" +
                                "    foreach (var product in products) {\n" +
                                "        Debug.Log($\"{product.ProductId}: {product.Metadata.LocalizedPriceString}\");\n" +
                                "    }\n" +
                                "});\n\n" +
                                "// Purchase a product\n" +
                                "BalancyPaymentManager.Instance.PurchaseProduct(\"my_product_id\", result => {\n" +
                                "    if (result.Status == PurchaseStatus.Success) {\n" +
                                "        // Grant the product to the player\n" +
                                "    }\n" +
                                "});\n\n" +
                                "// Restore purchases\n" +
                                "BalancyPaymentManager.Instance.RestorePurchases(results => {\n" +
                                "    Debug.Log($\"Restored {results.Count} purchases\");\n" +
                                "});\n" +
                                "```\n";
                
                File.WriteAllText(docsFile, content);
                AssetDatabase.Refresh();
            }
            
            // Open the documentation
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(docsFile.Replace(Application.dataPath, "Assets"));
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
            }
            else
            {
                Debug.LogError($"Failed to open documentation: {docsFile}");
            }
        }
    }
}