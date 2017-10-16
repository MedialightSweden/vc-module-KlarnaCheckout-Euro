﻿using Klarna.Api;
using Klarna.Checkout.Euro.Helpers;
using Klarna.Checkout.Euro.KlarnaApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using VirtoCommerce.Domain.Commerce.Model;
using VirtoCommerce.Domain.Order.Model;
using VirtoCommerce.Domain.Payment.Model;
using Address = VirtoCommerce.Domain.Commerce.Model.Address;
using Currency = Klarna.Api.Currency;

namespace Klarna.Checkout.Euro.Managers
{
    public class KlarnaCheckoutEuroPaymentMethod : VirtoCommerce.Domain.Payment.Model.PaymentMethod
    {
        private const string _klarnaModeStoreSetting = "Klarna.Checkout.Euro.Mode";
        private const string _klarnaAppKeyStoreSetting = "Klarna.Checkout.Euro.AppKey";
        private const string _klarnaAppSecretStoreSetting = "Klarna.Checkout.Euro.SecretKey";
        private const string _klarnaTermsUrl = "Klarna.Checkout.Euro.TermsUrl";
        private const string _klarnaCheckoutUrl = "Klarna.Checkout.Euro.CheckoutUrl";
        private const string _klarnaConfirmationUrl = "Klarna.Checkout.Euro.ConfirmationUrl";
        private const string _klarnaPaymentActionType = "Klarna.Checkout.Euro.PaymentActionType";
        private const string _klarnaPushUrl = "Klarna.Checkout.Euro.PushUrl";

        private const string _klarnaPurchaseCurrencyStoreSetting = "Klarna.Checkout.Euro.PurchaseCurrency";
        private const string _klarnaPurchaseCountyTwoLetterCodeStoreSetting = "Klarna.Checkout.Euro.PurchaseCountyTwoLetterCode";
        private const string _klarnaLocaleStoreSetting = "Klarna.Checkout.Euro.Locale";

        private const string _klarnaSalePaymentActionType = "Sale";

        public KlarnaCheckoutEuroPaymentMethod()
            : base("KlarnaCheckoutEuro")
        {
        }

        private string AppKey
        {
            get
            {
                return GetSetting(_klarnaAppKeyStoreSetting);
            }
        }

        private string AppSecret
        {
            get
            {
                return GetSetting(_klarnaAppSecretStoreSetting);
            }
        }

        private string TermsUrl
        {
            get
            {
                return GetSetting(_klarnaTermsUrl);
            }
        }

        private string ConfirmationUrl
        {
            get
            {
                return GetSetting(_klarnaConfirmationUrl);
            }
        }

        private string CheckoutUrl
        {
            get
            {
                return GetSetting(_klarnaCheckoutUrl);
            }
        }

        private string PaymentActionType
        {
            get
            {
                return GetSetting(_klarnaPaymentActionType);
            }
        }

        private string PurchaseCurrency
        {
            get
            {
                return GetSetting(_klarnaPurchaseCurrencyStoreSetting);
            }
        }

        private string PurchaseCountyTwoLetterCode
        {
            get
            {
                return GetSetting(_klarnaPurchaseCountyTwoLetterCodeStoreSetting);
            }
        }

        private string Locale
        {
            get
            {
                return GetSetting(_klarnaLocaleStoreSetting);
            }
        }

        private string PushUrl
        {
            get
            {
                return GetSetting(_klarnaPushUrl);
            }
        }

        private bool IsTestMode
        {
            get
            {
                return GetSetting(_klarnaModeStoreSetting).ToLower() == "test";
            }
        }

        public override PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.PreparedForm; }
        }

        public override PaymentMethodGroupType PaymentMethodGroupType
        {
            get { return PaymentMethodGroupType.Alternative; }
        }

        [JsonIgnore]
        public IConnector ApiConnector { get; set; }

        [JsonIgnore]
        public IKlarnaApi KlarnaApi { get; set; }

        public override ProcessPaymentResult ProcessPayment(ProcessPaymentEvaluationContext context)
        {
            ProcessPaymentResult retVal = new ProcessPaymentResult();

            if (context.Order != null && context.Store != null && context.Payment != null)
            {
                retVal = ProcessKlarnaOrder(context);
            }

            return retVal;
        }

        public override PostProcessPaymentResult PostProcessPayment(PostProcessPaymentEvaluationContext context)
        {
            return PostProcessKlarnaOrder(context);
        }

        public override CaptureProcessPaymentResult CaptureProcessPayment(CaptureProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            if (context.Payment == null)
                throw new ArgumentNullException("context.Payment");

            CaptureProcessPaymentResult retVal = new CaptureProcessPaymentResult();

            if (ApiConnector == null)
                ApiConnector = Connector.Create(AppSecret, CheckoutBaseUri);

            IConnector connector = ApiConnector;
            Order order = new Order(connector, context.Payment.OuterId);
            order.Fetch();

            string reservation = order.GetValue("reservation") as string;
            if (!string.IsNullOrEmpty(reservation))
            {
                try
                {
                    if (KlarnaApi == null)
                        InitializeKlarnaApi();

                    ActivateReservationResponse response = KlarnaApi.Activate(reservation);

                    retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Paid;
                    context.Payment.CapturedDate = DateTime.UtcNow;
                    context.Payment.IsApproved = true;
                    retVal.IsSuccess = true;
                    retVal.OuterId = context.Payment.OuterId = response.InvoiceNumber;
                }
                catch (Exception ex)
                {
                    retVal.ErrorMessage = ex.Message;
                }
            }
            else
            {
                retVal.ErrorMessage = "No reservation for this order";
            }

            return retVal;
        }

        public override VoidProcessPaymentResult VoidProcessPayment(VoidProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            if (context.Payment == null)
                throw new ArgumentNullException("context.Payment");

            VoidProcessPaymentResult retVal = new VoidProcessPaymentResult();

            if (!context.Payment.IsApproved && (context.Payment.PaymentStatus == PaymentStatus.Authorized || context.Payment.PaymentStatus == PaymentStatus.Cancelled))
            {
                if (ApiConnector == null)
                    ApiConnector = Connector.Create(AppSecret, CheckoutBaseUri);

                IConnector connector = ApiConnector;
                Order order = new Order(connector, context.Payment.OuterId);
                order.Fetch();

                string reservation = order.GetValue("reservation") as string;
                if (!string.IsNullOrEmpty(reservation))
                {
                    try
                    {
                        if (KlarnaApi == null)
                            InitializeKlarnaApi();

                        bool result = KlarnaApi.CancelReservation(reservation);
                        if (result)
                        {
                            retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Voided;
                            context.Payment.VoidedDate = context.Payment.CancelledDate = DateTime.UtcNow;
                            context.Payment.IsCancelled = true;
                            retVal.IsSuccess = true;
                        }
                        else
                        {
                            retVal.ErrorMessage = "Payment was not canceled, try later";
                        }
                    }
                    catch (Exception ex)
                    {
                        retVal.ErrorMessage = ex.Message;
                    }
                }
            }
            else if (context.Payment.IsApproved)
            {
                retVal.ErrorMessage = "Payment already approved, use refund";
                retVal.NewPaymentStatus = PaymentStatus.Paid;
            }
            else if (context.Payment.IsCancelled)
            {
                retVal.ErrorMessage = "Payment already canceled";
                retVal.NewPaymentStatus = PaymentStatus.Voided;
            }

            return retVal;
        }

        public override RefundProcessPaymentResult RefundProcessPayment(RefundProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            if (context.Payment == null)
                throw new ArgumentNullException("context.Payment");

            RefundProcessPaymentResult retVal = new RefundProcessPaymentResult();

            if (context.Payment.IsApproved && (context.Payment.PaymentStatus == PaymentStatus.Paid || context.Payment.PaymentStatus == PaymentStatus.Cancelled))
            {
                if (KlarnaApi == null)
                    InitializeKlarnaApi();

                string result = KlarnaApi.CreditInvoice(context.Payment.OuterId);

                if (!string.IsNullOrEmpty(result))
                {
                    retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Refunded;
                    context.Payment.CancelledDate = DateTime.UtcNow;
                    context.Payment.IsCancelled = true;
                    retVal.IsSuccess = true;
                }
            }

            return retVal;
        }

        public override ValidatePostProcessRequestResult ValidatePostProcessRequest(NameValueCollection queryString)
        {
            ValidatePostProcessRequestResult retVal = new ValidatePostProcessRequestResult();

            string klarnaOrderId = queryString["klarna_order_id"];
            string sid = queryString["sid"];

            if (!string.IsNullOrEmpty(klarnaOrderId) && !string.IsNullOrEmpty(sid))
            {
                retVal.IsSuccess = true;
                retVal.OuterId = klarnaOrderId;
            }

            return retVal;
        }

        #region Private Methods

        private ProcessPaymentResult ProcessKlarnaOrder(ProcessPaymentEvaluationContext context)
        {
            ProcessPaymentResult retVal = new ProcessPaymentResult();

            if (ApiConnector == null)
                ApiConnector = Connector.Create(AppSecret, CheckoutBaseUri);

            IConnector connector = ApiConnector;
            Order order = new Order(connector);

            //Create cart
            List<Dictionary<string, object>> cartItems = CreateKlarnaCartItems(context.Order);
            Dictionary<string, object> cart = new Dictionary<string, object> { { "items", cartItems } };

            Dictionary<string, object> merchant = new Dictionary<string, object>
                    {
                        { "id", AppKey },
                        { "terms_uri", $"{context.Store.Url}/{TermsUrl}"},
                        { "checkout_uri", $"{context.Store.Url}/{CheckoutUrl}"},
                        { "confirmation_uri", $"{context.Store.Url}/{ConfirmationUrl}?sid=123&orderId={context.Order.Id}&klarna_order_id={{checkout.order.id}}" },
                        { "push_uri", $"{PushUrl}/api/paymentcallback?sid=123&orderId={context.Order.Id}&klarna_order_id={{checkout.order.id}}" },
                        { "back_to_store_uri", context.Store.Url }
                    };

            Dictionary<string, object> layout = new Dictionary<string, object>
                    {
                        { "layout", "desktop" }
                    };

            Dictionary<string, object> data = new Dictionary<string, object>
                    {
                        { "purchase_country", PurchaseCountyTwoLetterCode},
                        { "purchase_currency", PurchaseCurrency},
                        { "locale", Locale},
                        { "cart", cart },
                        { "merchant", merchant},
                        { "gui", layout}
                    };

            order.Create(data);
            order.Fetch();

            //Gets snippet
            JObject gui = order.GetValue("gui") as JObject;
            string html = gui["snippet"].Value<string>();

            context.Order.Status = "Pending";
            retVal.IsSuccess = true;
            retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Pending;
            retVal.HtmlForm = html;
            retVal.OuterId = context.Payment.OuterId = order.GetValue("id") as string;
            return retVal;
        }

        private Dictionary<string, string> countryCodesMapping = new Dictionary<string, string>()
        {
            {"AFG", "AF"}, // Afghanistan
            {"ALB", "AL"}, // Albania
            {"ARE", "AE"}, // U.A.E.
            {"ARG", "AR"}, // Argentina
            {"ARM", "AM"}, // Armenia
            {"AUS", "AU"}, // Australia
            {"AUT", "AT"}, // Austria
            {"AZE", "AZ"}, // Azerbaijan
            {"BEL", "BE"}, // Belgium
            {"BGD", "BD"}, // Bangladesh
            {"BGR", "BG"}, // Bulgaria
            {"BHR", "BH"}, // Bahrain
            {"BIH", "BA"}, // Bosnia and Herzegovina
            {"BLR", "BY"}, // Belarus
            {"BLZ", "BZ"}, // Belize
            {"BOL", "BO"}, // Bolivia
            {"BRA", "BR"}, // Brazil
            {"BRN", "BN"}, // Brunei Darussalam
            {"CAN", "CA"}, // Canada
            {"CHE", "CH"}, // Switzerland
            {"CHL", "CL"}, // Chile
            {"CHN", "CN"}, // People's Republic of China
            {"COL", "CO"}, // Colombia
            {"CRI", "CR"}, // Costa Rica
            {"CZE", "CZ"}, // Czech Republic
            {"DEU", "DE"}, // Germany
            {"DNK", "DK"}, // Denmark
            {"DOM", "DO"}, // Dominican Republic
            {"DZA", "DZ"}, // Algeria
            {"ECU", "EC"}, // Ecuador
            {"EGY", "EG"}, // Egypt
            {"ESP", "ES"}, // Spain
            {"EST", "EE"}, // Estonia
            {"ETH", "ET"}, // Ethiopia
            {"FIN", "FI"}, // Finland
            {"FRA", "FR"}, // France
            {"FRO", "FO"}, // Faroe Islands
            {"GBR", "GB"}, // United Kingdom
            {"GEO", "GE"}, // Georgia
            {"GRC", "GR"}, // Greece
            {"GRL", "GL"}, // Greenland
            {"GTM", "GT"}, // Guatemala
            {"HKG", "HK"}, // Hong Kong S.A.R.
            {"HND", "HN"}, // Honduras
            {"HRV", "HR"}, // Croatia
            {"HUN", "HU"}, // Hungary
            {"IDN", "ID"}, // Indonesia
            {"IND", "IN"}, // India
            {"IRL", "IE"}, // Ireland
            {"IRN", "IR"}, // Iran
            {"IRQ", "IQ"}, // Iraq
            {"ISL", "IS"}, // Iceland
            {"ISR", "IL"}, // Israel
            {"ITA", "IT"}, // Italy
            {"JAM", "JM"}, // Jamaica
            {"JOR", "JO"}, // Jordan
            {"JPN", "JP"}, // Japan
            {"KAZ", "KZ"}, // Kazakhstan
            {"KEN", "KE"}, // Kenya
            {"KGZ", "KG"}, // Kyrgyzstan
            {"KHM", "KH"}, // Cambodia
            {"KOR", "KR"}, // Korea
            {"KWT", "KW"}, // Kuwait
            {"LAO", "LA"}, // Lao P.D.R.
            {"LBN", "LB"}, // Lebanon
            {"LBY", "LY"}, // Libya
            {"LIE", "LI"}, // Liechtenstein
            {"LKA", "LK"}, // Sri Lanka
            {"LTU", "LT"}, // Lithuania
            {"LUX", "LU"}, // Luxembourg
            {"LVA", "LV"}, // Latvia
            {"MAC", "MO"}, // Macao S.A.R.
            {"MAR", "MA"}, // Morocco
            {"MCO", "MC"}, // Principality of Monaco
            {"MDV", "MV"}, // Maldives
            {"MEX", "MX"}, // Mexico
            {"MKD", "MK"}, // Macedonia (FYROM)
            {"MLT", "MT"}, // Malta
            {"MNE", "ME"}, // Montenegro
            {"MNG", "MN"}, // Mongolia
            {"MYS", "MY"}, // Malaysia
            {"NGA", "NG"}, // Nigeria
            {"NIC", "NI"}, // Nicaragua
            {"NLD", "NL"}, // Netherlands
            {"NOR", "NO"}, // Norway
            {"NPL", "NP"}, // Nepal
            {"NZL", "NZ"}, // New Zealand
            {"OMN", "OM"}, // Oman
            {"PAK", "PK"}, // Islamic Republic of Pakistan
            {"PAN", "PA"}, // Panama
            {"PER", "PE"}, // Peru
            {"PHL", "PH"}, // Republic of the Philippines
            {"POL", "PL"}, // Poland
            {"PRI", "PR"}, // Puerto Rico
            {"PRT", "PT"}, // Portugal
            {"PRY", "PY"}, // Paraguay
            {"QAT", "QA"}, // Qatar
            {"ROU", "RO"}, // Romania
            {"RUS", "RU"}, // Russia
            {"RWA", "RW"}, // Rwanda
            {"SAU", "SA"}, // Saudi Arabia
            {"SCG", "CS"}, // Serbia and Montenegro (Former)
            {"SEN", "SN"}, // Senegal
            {"SGP", "SG"}, // Singapore
            {"SLV", "SV"}, // El Salvador
            {"SRB", "RS"}, // Serbia
            {"SVK", "SK"}, // Slovakia
            {"SVN", "SI"}, // Slovenia
            {"SWE", "SE"}, // Sweden
            {"SYR", "SY"}, // Syria
            {"TAJ", "TJ"}, // Tajikistan
            {"THA", "TH"}, // Thailand
            {"TKM", "TM"}, // Turkmenistan
            {"TTO", "TT"}, // Trinidad and Tobago
            {"TUN", "TN"}, // Tunisia
            {"TUR", "TR"}, // Turkey
            {"TWN", "TW"}, // Taiwan
            {"UKR", "UA"}, // Ukraine
            {"URY", "UY"}, // Uruguay
            {"USA", "US"}, // United States
            {"UZB", "UZ"}, // Uzbekistan
            {"VEN", "VE"}, // Bolivarian Republic of Venezuela
            {"VNM", "VN"}, // Vietnam
            {"YEM", "YE"}, // Yemen
            {"ZAF", "ZA"}, // South Africa
            {"ZWE", "ZW"} // Zimbabwe
        };

        private PostProcessPaymentResult PostProcessKlarnaOrder(PostProcessPaymentEvaluationContext context)
        {
            PostProcessPaymentResult retVal = new PostProcessPaymentResult();

            if (ApiConnector == null)
                ApiConnector = Connector.Create(AppSecret, CheckoutBaseUri);

            IConnector connector = ApiConnector;
            Order order = new Order(connector, context.OuterId);
            order.Fetch();
            string status = order.GetValue("status") as string;

            JObject gui = order.GetValue("gui") as JObject;
            string html = gui["snippet"].Value<string>();

            if (status == "checkout_complete")
            {
                Dictionary<string, object> data = new Dictionary<string, object> { { "status", "created" } };
                order.Update(data);
                //order.Fetch();
                status = order.GetValue("status") as string;

                object value = order.GetValue("shipping_address");

                JObject shippingAddress = JObject.FromObject(value);

                if (context.Order.Addresses == null || context.Order.Addresses.Count < 1)
                {
                    if (context.Order.Addresses == null)
                    {
                        context.Order.Addresses = new List<Address>();
                    }

                    context.Order.Addresses.Add(new Address { AddressType = AddressType.Shipping });
                }

                Address address = context.Order.Addresses.First(add => add.AddressType == AddressType.Shipping);

                address.FirstName = shippingAddress["given_name"]?.Value<string>();

                address.LastName = shippingAddress["family_name"]?.Value<string>();

                address.Name = shippingAddress["care_of"]?.Value<string>();

                address.Line1 = shippingAddress["street_address"]?.Value<string>() ??
                                (shippingAddress["street_name"] != null && shippingAddress["street_number"] != null ? $@"{shippingAddress["street_name"]?.Value<string>()} {shippingAddress["street_number"]?.Value<string>()}" : null) ??
                                shippingAddress["street_name"]?.Value<string>() ?? shippingAddress["street_number"]?.Value<string>();

                address.Organization = shippingAddress["organization_name"]?.Value<string>();

                address.Zip = shippingAddress["postal_code"]?.Value<string>();

                address.City = shippingAddress["city"]?.Value<string>();

                address.CountryCode = shippingAddress["country"]?.Value<string>();

                if (address.CountryCode != null)
                {
                    RegionInfo regionInfo = new RegionInfo(address.CountryCode);

                    address.CountryCode = regionInfo.TwoLetterISORegionName;

                    address.CountryName = regionInfo.EnglishName;
                }

                address.Email = shippingAddress["email"]?.Value<string>();

                address.Phone = shippingAddress["phone"]?.Value<string>();

                address.RegionName = "N/A";

                address.RegionId = "N/A";
            }

            if (status == "created" && IsSale())
            {
                CaptureProcessPaymentResult result = CaptureProcessPayment(new CaptureProcessPaymentEvaluationContext { Payment = context.Payment });

                context.Order.Status = "Paid";

                retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Paid;
                context.Payment.OuterId = result.OuterId;
                context.Payment.IsApproved = true;
                context.Payment.CapturedDate = DateTime.UtcNow;
                retVal.IsSuccess = true;

            }
            else if (status == "created")
            {
                context.Order.Status = "Authorized";

                retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Authorized;
                context.Payment.OuterId = retVal.OuterId = context.OuterId;
                context.Payment.AuthorizedDate = DateTime.UtcNow;
                retVal.IsSuccess = true;
            }
            else
            {
                retVal.ErrorMessage = "order not created";
            }

            retVal.ReturnUrl = html;

            retVal.OrderId = context.Order.Id;
            return retVal;
        }

        private List<Dictionary<string, object>> CreateKlarnaCartItems(CustomerOrder order)
        {
            List<Dictionary<string, object>> cartItems = new List<Dictionary<string, object>>();
            foreach (LineItem lineItem in order.Items)
            {
                Dictionary<string, object> addedItem = new Dictionary<string, object>();

                addedItem.Add("type", "physical");

                if (!string.IsNullOrEmpty(lineItem.Name))
                {
                    addedItem.Add("name", lineItem.Name);
                }
                if (lineItem.Quantity > 0)
                {
                    addedItem.Add("quantity", lineItem.Quantity);
                }
                if (lineItem.Price > 0)
                {
                    addedItem.Add("unit_price", (lineItem.PlacedPriceWithTax * 100).Round());
                    //addedItem.Add("total_price_excluding_tax", (lineItem.Price * lineItem.Quantity * 100).Round());
                }

                if (lineItem.TaxPercentRate > 0)
                {
                    //addedItem.Add("total_price_including_tax", ((lineItem.Price * lineItem.Quantity + lineItem.Tax) * 100).Round());
                    //addedItem.Add("total_tax_amount", (lineItem.Tax * 100, MidpointRounding.AwayFromZero).Round());
                    //addedItem.Add("tax_rate", (lineItem.TaxDetails.Sum(td => td.Rate) * 10000).Round());
                    addedItem.Add("tax_rate", (lineItem.TaxPercentRate * 100).Round());
                }
                else
                {
                    addedItem.Add("tax_rate", 0);
                }

                addedItem.Add("discount_rate", 0);
                addedItem.Add("reference", lineItem.ProductId);

                cartItems.Add(addedItem);
            }

            if (order.Shipments != null && order.Shipments.Any(s => s.Sum > 0))
            {
                foreach (Shipment shipment in order.Shipments.Where(s => s.Sum > 0))
                {
                    Dictionary<string, object> addedItem = new Dictionary<string, object>();

                    addedItem.Add("type", "shipping_fee");
                    addedItem.Add("reference", "SHIPPING");
                    addedItem.Add("name", "Shipping Fee");
                    addedItem.Add("quantity", 1);
                    addedItem.Add("unit_price", (shipment.Sum * 100).Round());

                    addedItem.Add("tax_rate", 0);

                    cartItems.Add(addedItem);
                }
            }

            return cartItems;
        }

        private bool IsSale()
        {
            return PaymentActionType.Equals(_klarnaSalePaymentActionType);
        }

        private Uri CheckoutBaseUri
        {
            get
            {
                return IsTestMode ? Connector.TestBaseUri : Connector.BaseUri;
            }
        }

        private Configuration GetConfiguration()
        {
            return new Configuration(GetCountryCode(), GetLanguageCode(), GetCurrencyCode(), GetEncoding());
        }

        private Encoding GetEncoding()
        {
            Encoding retVal = Encoding.Sweden;

            switch (Locale)
            {
                case "da-dk":
                    retVal = Encoding.Denmark;
                    break;

                case "de-at":
                    retVal = Encoding.Austria;
                    break;

                case "nb-no":
                    retVal = Encoding.Norway;
                    break;

                case "fi-fi":
                    retVal = Encoding.Finland;
                    break;

                case "de-de":
                    retVal = Encoding.Germany;
                    break;

                case "sv-se":
                    retVal = Encoding.Sweden;
                    break;
            }

            return retVal;
        }

        private Currency.Code GetCurrencyCode()
        {
            Currency.Code retVal = Klarna.Api.Currency.Code.SEK;

            switch (PurchaseCurrency)
            {
                case "DKK":
                    retVal = Klarna.Api.Currency.Code.DKK;
                    break;

                case "EUR":
                    retVal = Klarna.Api.Currency.Code.EUR;
                    break;

                case "NOK":
                    retVal = Klarna.Api.Currency.Code.NOK;
                    break;

                case "SEK":
                    retVal = Klarna.Api.Currency.Code.SEK;
                    break;
            }

            return retVal;
        }

        private Language.Code GetLanguageCode()
        {
            Language.Code retVal = Language.Code.SV;

            switch (Locale)
            {
                case "da-dk":
                    retVal = Language.Code.DA;
                    break;

                case "en-us":
                    retVal = Language.Code.EN;
                    break;

                case "nb-no":
                    retVal = Language.Code.NB;
                    break;

                case "fi-fi":
                    retVal = Language.Code.FI;
                    break;

                case "de-at":
                case "de-de":
                    retVal = Language.Code.DE;
                    break;

                    //case "sv-se": // Code.SV is the default code
                    //    retVal = Language.Code.SV;
                    //    break;
            }

            return retVal;
        }

        private Country.Code GetCountryCode()
        {
            Country.Code retVal = Country.Code.SE;

            switch (PurchaseCountyTwoLetterCode)
            {
                case "DK":
                    retVal = Country.Code.DK;
                    break;

                case "AT":
                    retVal = Country.Code.AT;
                    break;

                case "NO":
                    retVal = Country.Code.NO;
                    break;

                case "FI":
                    retVal = Country.Code.FI;
                    break;

                case "DE":
                    retVal = Country.Code.DE;
                    break;

                case "SE":
                    retVal = Country.Code.SE;
                    break;
            }

            return retVal;
        }

        private void InitializeKlarnaApi()
        {
            Configuration configuration = GetConfiguration();
            configuration.Eid = Convert.ToInt32(AppKey);
            configuration.Secret = AppSecret;
            configuration.IsLiveMode = !IsTestMode;

            Api.Api api = new Api.Api(configuration);

            KlarnaApi = new KlarnaApiImpl(api);
        }

        #endregion
    }
}