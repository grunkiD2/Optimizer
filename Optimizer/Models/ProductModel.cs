using System;
using Optimizer.Helpers;

namespace Optimizer.Models
{
    /// <summary>
    /// Represents a product or item in the application.
    /// Used as a data transfer object for UI bindings and business logic operations.
    /// </summary>
    public class ProductModel : Observable
    {
        private string _productName;
        private decimal? _price;
        private int _stockQuantity;
        private Uri? _imageUrl;
        private bool _isActive;

        /// <summary>
        /// Initializes a new instance of the ProductModel class with default values.
        /// </summary>
        public ProductModel()
        {
            IsActive = true;
        }

        /// <summary>
        /// Initializes a new instance of the ProductModel class using provided data.
        /// </summary>
        /// <param name="name">The product name.</param>
        /// <param name="price">The unit price (nullable).</param>
        /// <param name="stock">Current stock quantity.</param>
        public ProductModel(string name, decimal? price = null, int stock = 0)
        {
            ProductName = name ?? throw new ArgumentNullException(nameof(name));
            Price = price;
            StockQuantity = stock;
        }

        /// <summary>
        /// Gets or sets the product identifier.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the display name of the product.
        /// Setting this property automatically raises PropertyChanged for UI updates.
        /// </summary>
        public string ProductName
        {
            get => _productName;
            set => Set(ref _productName, value);
        }

        /// <summary>
        /// Gets or sets the unit price of the product in USD.
        /// Accepts null values for optional pricing items.
        /// </summary>
        public decimal? Price
        {
            get => _price;
            set => Set(ref _price, value);
        }

        /// <summary>
        /// Gets or sets the current stock quantity available.
        /// Negative values indicate backorder status.
        /// </summary>
        public int StockQuantity
        {
            get => _stockQuantity;
            set => Set(ref _stockQuantity, value);
        }

        /// <summary>
        /// Gets or sets the URL to product image resource.
        /// Can be relative (e.g., /Assets/Products/item1.png) or absolute.
        /// </summary>
        public Uri? ImageUrl
        {
            get => _imageUrl;
            set => Set(ref _imageUrl, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the product is currently active.
        /// False items are hidden from search results and menus.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set => Set(ref _isActive, value);
        }

        /// <summary>
        /// Gets a computed property indicating if stock is available for purchase.
        /// Read-only; raises PropertyChanged when StockQuantity or IsActive changes.
        /// </summary>
        public bool IsInStock => StockQuantity > 0 && IsActive;

        /// <summary>
        /// Formats the product name and price for display purposes.
        /// Returns null if ProductName is empty.
        /// </summary>
        /// <returns>Formatted string "ProductName - $Price" or just ProductName.</returns>
        public override string ToString()
        {
            return string.IsNullOrEmpty(ProductName) ? null : 
                   !string.IsNullOrEmpty(ProductName) && Price.HasValue ?
                       $"{ProductName} - ${Price.Value:N2}" :
                       ProductName;
        }

        /// <summary>
        /// Creates a deep copy of the product with new instance references.
        /// </summary>
        /// <returns>A new ProductModel instance with copied data.</returns>
        public ProductModel Clone()
        {
            return new ProductModel(
                ProductName,
                Price,
                StockQuantity
            )
            {
                Id = Id,
                IsActive = IsActive,
                ImageUrl = ImageUrl?.ToString() != null ? new Uri(ImageUrl.ToString()) : null
            };
        }
    }
}
