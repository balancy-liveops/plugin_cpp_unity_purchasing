# Balancy Purchasing for Unity

**Seamless In-App Purchasing with Receipt Validation & Reward Delivery**

Balancy Purchasing is a dedicated module built on top of **Unity Purchasing**. It streamlines the entire purchase flow — from initiating transactions to validating receipts and granting rewards — ensuring a reliable and smooth experience for both players and developers.

## Features

- **Smooth Purchase Flow**: Simplified in-app purchase experience built on Unity Purchasing  
- **Receipt Validation**: Secure server-side validation of purchases  
- **Automatic Reward Delivery**: Instantly grant items, currencies, or perks after successful validation  
- **Error Handling**: Gracefully handles failed transactions and retries  
- **Balancy Integration**: Works seamlessly with Balancy’s LiveOps & economy system  
- **Cross-Platform Support**: Compatible with all platforms supported by Unity IAP  

## Installation

### Via OpenUPM

The recommended way to install Balancy Purchasing is through OpenUPM:

```bash
openupm add co.balancy.unity-purchasing
```

### Via Git URL

You can also install this package through the Unity Package Manager using Git:

1. Open **Unity Package Manager**  
2. Click the **“+”** button  
3. Select **“Add package from git URL”**  
4. Enter:  
   ```
   https://github.com/balancy-liveops/plugin_cpp_unity_purchasing.git
   ```

## Getting Started

1. Make sure Unity Purchasing is installed (this package will add it as a dependency automatically).  
2. After importing, go to **Tools → Balancy → Purchasing Config** to configure purchase settings.  
3. Define your in-app products in the Balancy Dashboard.  
4. Follow the [integration guide](https://en.docsv2.balancy.dev) to connect purchase events to your game logic.  

## Workflow

Balancy Purchasing automatically handles:

- Initializing Unity IAP  
- Processing user purchases  
- Validating receipts securely  
- Granting rewards or items from your Balancy economy setup  

This means less boilerplate code and fewer integration pitfalls.

## Documentation

For detailed setup instructions and examples, see:  
- [Balancy Docs](https://en.docsv2.balancy.dev)  
- [Release Notes](https://en.docsv2.balancy.dev/release_notes/)  

## Support

If you encounter issues or have questions:  
- [GitHub Issues](https://github.com/balancy-liveops/plugin_cpp_unity_purchasing/issues)  
- Email: [contact@balancy.co](mailto:contact@balancy.co)  
- [Discord Community](https://discord.gg/balancy)  

## License

This package is licensed under the MIT License – see the LICENSE file for details.

## Requirements

- Unity **2022.3** or newer  
- Unity Purchasing **5.0.1** (installed automatically as a dependency)  
