using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Xml;

namespace Sync1CAspNetMvc.Controllers
{
    #region Controllers
    /// <summary>
    /// ВАЖНО!!!
    /// Данный код представлен только для примера обмена сайт с 1С. 
    /// Автор не нечет отвественности за сбои в вашем решении. 
    /// Проводите полноценное тестирование обмена сайта с 1С.
    /// </summary>
    public class SyncController : Controller
    {
        #region Consts
        const string TypeProducts = "catalog";
        const string TypeOrders = "sale";

        const string ModeCheckAuth = "checkauth";
        const string ModeInit = "init";
        const string ModeUploadFile = "file";
        const string ModeImport = "import";
        const string ModeGetOrders = "query";
        const string ModeGetOrdersSuccess = "success";

        const string ResultSuccess = "success";
        const string ResultFailure = "failure";

        const string ReturnSeparator = "\n";

        const string IsZipParam = "zip";
        const string MaxFileLengthParam = "file_limit" ;
        const string IsZip = "zip=no";
        const int MaxFileLength = 1024;

        const string SaveFilesDir = "Upload1C";
        const string SaveTempFilesDir = "Temp";

        const string ProductsFileName = "import0_1.xml";
        const string ProductsPricesFileName = "offers0_1.xml";
        const string OrdersFileNameStart = "orders-";

        const string ValutaRub = "RUB";
        #endregion

        private readonly IAuth _auth;
        private readonly IProducts _products;
        private readonly IOrders _orders;

        public SyncController()
        {
            _auth = new AuthFake();
            _products = new ProductsFake();
            _orders = new OrdersFake();
        }

        public string Exchange1C(string type = "", string mode = "", string filename = "")
        {
            // Логинимся
            if (mode == ModeCheckAuth) return Auth();
            
            // Проверяем залогинен или нет
            if (!CheckAuth()) return ResultFailure;

            // Если загрузка товаров
            if (type == TypeProducts)
            {
                // Если инициализация загрузки
                if (mode == ModeInit) return InitProducts();

                // Если загрузка файла
                if (mode == ModeUploadFile) return UploadFile(filename);

                // Если импорт
                if (mode == ModeImport) return MoveUploadFile(filename);
            }
            // Если синхронизация заказов
            else if (type == TypeOrders)
            {
                // Если инициализация загрузки
                if (mode == ModeInit) return InitOrders();

                // Если отдаем заказы
                if (mode == ModeGetOrders) return GetOrders();
                if (mode == ModeGetOrdersSuccess) return ResultSuccess;

                // Если загрузка файла
                if (mode == ModeUploadFile) return UploadFile(filename);

                // Если загрузка файла
                if (mode == ModeImport) return MoveUploadFile(filename);
            }

            return ResultFailure;
        }

        private string Auth()
        {
            string cookieName;
            string cookieValue;

            if (_auth.CheckAuthBasic(Request, out cookieName, out cookieValue))
            {
                return GetResponseString(ResultSuccess, cookieName, cookieValue);
            }

            return ResultFailure;
        }
        private bool CheckAuth()
        {
            return _auth.IsAuth(Request);
        }
        
        private string GetResponseString(params string[] values)
        {
            return string.Join(ReturnSeparator, values);
        }
        private string GetParam(string name, object value)
        {
            return name + "=" + (value != null ? value.ToString() : "");
        }

        private string InitProducts()
        {
            var tempDir = GetSaveTempDir();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

            return GetResponseString(GetParam(IsZipParam, IsZip), GetParam(MaxFileLengthParam, MaxFileLength));
        }
        private string UploadFile(string fileName)
        {
            if (Request.InputStream == null) return ResultFailure;

            try
            {
                var saveTempFileName = GetSaveTempFileName(fileName);

                CheckFileDir(saveTempFileName);

                using (var fileStream = System.IO.File.Open(saveTempFileName, FileMode.Append))
                {
                    Request.InputStream.CopyTo(fileStream);
                }

                return ResultSuccess;
            }
            catch (Exception)
            {
                return ResultFailure;
            }
        }
        private string MoveUploadFile(string fileName)
        {
            try
            {
                // Копируем темповский файл в основную директорию
                var saveTempFileName = GetSaveTempFileName(fileName);
                var saveFileName = GetSaveFileName(fileName);

                CheckFileDir(saveFileName);

                if (System.IO.File.Exists(saveFileName)) System.IO.File.Delete(saveFileName);
                System.IO.File.Move(saveTempFileName, saveFileName);

                // Загружаем товары
                if (IsProductsPricesFile(fileName))
                {
                    var products = ParseProductsPrices(saveFileName);
                    _products.Upload(products);
                }
                // Обновляем заказы
                else if (IsOrdersFile(fileName))
                {
                    var orders = ParseOrders(saveFileName);
                    _orders.Update(orders);

                    System.IO.File.Delete(saveFileName);
                }

                return ResultSuccess;
            }
            catch (Exception)
            {
                return ResultFailure;
            }
        }

        private string InitOrders()
        {
            var tempDir = GetSaveTempDir();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

            return GetResponseString(GetParam(IsZipParam, IsZip), GetParam(MaxFileLengthParam, MaxFileLength));
        }
        private string  GetOrders()
        {
            var orders = _orders.GetAll();
            return GetOrdersXml(orders);
        }

        private string GetProductsPricesFile()
        {
            return string.Format("/{0}/{1}", SaveFilesDir, ProductsPricesFileName);
        }
        private string GetSaveFileName(string fileName)
        {
            var saveFilePath = string.Format("/{0}/{1}", SaveFilesDir, fileName);
            return Server.MapPath(saveFilePath);
        }
        private string GetSaveTempFileName(string fileName)
        {
            var saveFilePath = string.Format("/{0}/{1}/{2}", SaveFilesDir, SaveTempFilesDir, fileName);
            return Server.MapPath(saveFilePath);
        }
        private string GetSaveTempDir()
        {
            return CheckDir(Server.MapPath(string.Format("/{0}/{1}", SaveFilesDir, SaveTempFilesDir)));
        }
        private string GetSaveDir()
        {
            return CheckDir(Server.MapPath(string.Format("/{0}", SaveFilesDir)));
        }
        private string CheckFileDir(string fileName)
        {
            var fileInfo = new FileInfo(fileName);
            if (!Directory.Exists(fileInfo.DirectoryName))
                Directory.CreateDirectory(fileInfo.DirectoryName);
            return fileName;
        }
        private string CheckDir(string dirName)
        {
            if (!Directory.Exists(dirName))
                Directory.CreateDirectory(dirName);
            return dirName;
        }

        #region Xml
        private bool IsProductsPricesFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            return ProductsPricesFileName.Equals(fileName);
        }
        /// <summary>
        /// В текущем примере при парсинге используем только файл offers0_1.xml. В данном файле представлены название, артикул и цена товара.
        /// В файле import0_1.xml представлены дополнительные свойства, картинки товаров. Мы не будет рассматривать парсинг этого файла в данном примере. Он делается по аналогии с данным парсингом.
        /// </summary>
        /// <returns></returns>
        private Product[] ParseProductsPrices(string productsXmlFile)
        {
            var products = new List<Product>();

            var doc = new XmlDocument();
            doc.Load(productsXmlFile);

            var priceTypeId = ParseProductsFindPriceType(doc);
            if (string.IsNullOrEmpty(priceTypeId)) throw new Exception("Не удалось найти Тип цены");

            var productsNodes = XmlFindNode(doc.DocumentElement, "Предложения");

            foreach (XmlNode productNode in productsNodes.ChildNodes)
            {
                var product = new Product()
                {
                    Id = XmlGetNodeInnerText(productNode, "Ид"),
                    Article = XmlGetNodeInnerText(productNode, "Артикул"),
                    Name = XmlGetNodeInnerText(productNode, "Наименование"),
                    Price = ParseProductsFindPrice(productNode),
                    Count = XmlGetNodeInnerTextInt(productNode, "Количество"),
                };

                if (double.IsNaN(product.Price) || product.Count == 0) continue;

                products.Add(product);
            }

            return products.ToArray();
        }
        private string ParseProductsFindPriceType(XmlDocument doc)
        {
            var pricesNode = XmlFindNode(doc.DocumentElement, "ТипыЦен");

            foreach (XmlNode priceNode in pricesNode.ChildNodes)
            {
                var priceType = XmlGetNodeInnerText(priceNode, "Валюта");

                if (priceType.Equals(ValutaRub, StringComparison.OrdinalIgnoreCase))
                {
                    var priceId = XmlGetNodeInnerText(priceNode, "Ид");
                    if (string.IsNullOrEmpty(priceId)) continue;
                    return priceId;
                }
            }

            return null;
        }
        private double ParseProductsFindPrice(XmlNode productNode)
        {
            var pricesNodes = XmlFindNode(productNode, "Цены");
                
            foreach (XmlNode priceNode in pricesNodes.ChildNodes)
            {
                var priceType = XmlGetNodeInnerText(priceNode, "Валюта");

                if (priceType.Equals(ValutaRub, StringComparison.OrdinalIgnoreCase))
                {
                    return XmlGetNodeInnerTextDouble(priceNode, "ЦенаЗаЕдиницу");
                }
            }

            return double.NaN;
        }
       

        private bool IsOrdersFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            return fileName.StartsWith(OrdersFileNameStart);
        }
        private Order[] ParseOrders(string ordersXmlFile)
        {
            var orders = new List<Order>();

            var doc = new XmlDocument();
            doc.Load(ordersXmlFile);

            foreach (XmlNode orderNode in doc.DocumentElement.ChildNodes)
            {
                var order = new Order();

                order.Id = XmlGetNodeInnerText(orderNode, "Номер");

                var kontragentNode = XmlFindNodeInChilds(orderNode, "Контрагенты");
                if (kontragentNode != null && kontragentNode.HasChildNodes)
                    order.Fio = XmlGetNodeInnerText(kontragentNode.FirstChild, "ПолноеНаименование");

                var productsNode = XmlFindNodeInChilds(orderNode, "Товары");
                if (productsNode != null)
                    order.Products = productsNode.ChildNodes
                        .Cast<XmlNode>()
                        .Select(item => new Product()
                        {
                            Id = XmlGetNodeInnerText(item, "Ид"),
                            Article = XmlGetNodeInnerText(item, "Артикул"),
                            Name = XmlGetNodeInnerText(item, "Наименование"),
                            Price = XmlGetNodeInnerTextDouble(item, "ЦенаЗаЕдиницу"),
                            Count = XmlGetNodeInnerTextInt(item, "Количество"),
                        })
                        .ToArray();

                orders.Add(order);
            }

            return orders.ToArray();
        }
        private string GetOrdersXml(Order[] orders)
        {
            var ordersXml = new StringBuilder();

            ordersXml.AppendFormat(@"<?xml version=""1.0"" encoding=""{0}""?>", Encoding.Default.HeaderName);
            ordersXml.AppendFormat(@"<КоммерческаяИнформация ВерсияСхемы=""2.10"" ДатаФормирования=""{0:yyyy-MM-dd}"">", DateTime.Now);

            foreach (var order in orders)
            {
                ordersXml.AppendFormat(@"<Документ>");
                ordersXml.AppendFormat(@"<Ид>{0}</Ид>", order.IdGuid);
                ordersXml.AppendFormat(@"<Номер>{0}</Номер>", order.Id);
                ordersXml.AppendFormat(@"<Дата>{0:yyyy-MM-dd}</Дата>", order.DateCreate);
                ordersXml.AppendFormat(@"<Время>{0:HH:mm:ss}</Время>", order.DateCreate);
                ordersXml.AppendFormat(@"<ХозОперация>Заказ товара</ХозОперация>");
                ordersXml.AppendFormat(@"<Роль>Продавец</Роль>");
                ordersXml.AppendFormat(@"<Валюта>{0}</Валюта>", ValutaRub);
                ordersXml.AppendFormat(@"<Сумма>{0:0.00}</Сумма>", order.TotalPrice);
                ordersXml.AppendFormat(@"<Контрагенты>");
                ordersXml.AppendFormat(@"<Контрагент>");
                ordersXml.AppendFormat(@"<Роль>Покупатель</Роль>");
                ordersXml.AppendFormat(@"<Наименование>{0}</Наименование>", order.Fio);
                ordersXml.AppendFormat(@"<ПолноеНаименование>{0}</ПолноеНаименование>", order.Fio);
                ordersXml.AppendFormat(@"</Контрагент>");
                ordersXml.AppendFormat(@"</Контрагенты>");

                ordersXml.AppendFormat(@"<Товары>");

                foreach (var product in order.Products)
                {
                    ordersXml.AppendFormat(@"<Товар>");
                    ordersXml.AppendFormat(@"<ИдентификаторТовара>{0}</ИдентификаторТовара>", product.Article);
                    ordersXml.AppendFormat(@"<Наименование>{0} ({1})</Наименование>", product.Name, product.Article);
                    ordersXml.AppendFormat(@"<БазоваяЕдиница Код=""796"" НаименованиеПолное=""Штука"" МеждународноеСокращение=""PCE"">шт</БазоваяЕдиница>");
                    ordersXml.AppendFormat(@"<ЦенаЗаЕдиницу>{0:0.00}</ЦенаЗаЕдиницу>", product.Price);
                    ordersXml.AppendFormat(@"<Количество>{0}</Количество>", product.Count);
                    ordersXml.AppendFormat(@"<Сумма>{0:0.00}</Сумма>", product.TotalPrice);
                    ordersXml.AppendFormat(@"</Товар>");
                }

                ordersXml.AppendFormat(@"</Товары>");

                ordersXml.AppendFormat(@"</Документ>");
            }

            ordersXml.AppendFormat(@"</КоммерческаяИнформация>");

            return ordersXml.ToString();
        }

        private int XmlGetNodeInnerTextInt(XmlNode node, string childNodeName)
        {
            var innerText = XmlGetNodeInnerText(node, childNodeName);
            int valueInt;
            if (!int.TryParse(innerText, out valueInt)) return 0;
            return valueInt;
        }
        private double XmlGetNodeInnerTextDouble(XmlNode node, string childNodeName)
        {
            var innerText = XmlGetNodeInnerText(node, childNodeName);
            if (string.IsNullOrEmpty(innerText)) return double.NaN;

            double valueDouble;
            if (!double.TryParse(innerText.Replace('.', ','), out valueDouble)) return double.NaN;
            return valueDouble;
        }
        private string XmlGetNodeInnerText(XmlNode node, string childNodeName)
        {
            return XmlGetNodeInnerText(XmlFindNodeInChilds(node, childNodeName));
        }
        private string XmlGetNodeInnerText(XmlNode node)
        {
            if (node == null) return "";
            return node.InnerText;
        }
        private XmlNode XmlFindNodeInChilds(XmlNode parentNode, string name)
        {
            var node = parentNode.ChildNodes.Cast<XmlNode>().FirstOrDefault(item => item.Name.Equals(name));
            return node;
        }
        private XmlNode XmlFindNode(XmlNode parentNode, string name)
        {
            var node = parentNode.ChildNodes.Cast<XmlNode>().FirstOrDefault(item => item.Name.Equals(name));
            if (node != null) return node;

            foreach (XmlNode childNode in parentNode.ChildNodes)
            {
                node = XmlFindNode(childNode, name);
                if (node != null) return node;
            }

            return null;
        }
        #endregion
    }
    #endregion

    #region Abstract
    public interface IAuth
    {
        bool CheckAuthBasic(HttpRequestBase request, out string cookieName, out string cookieValue);
        bool IsAuth(HttpRequestBase request);
    }
    public interface IOrders
    {
        Order[] GetAll();
        bool Update(Order[] orders);
    }
    public interface IProducts
    {
        bool Upload(Product[] products);
    }
    #endregion

    #region Objects
    public class Order
    {
        public string IdGuid { get; set; }
        public string Id { get; set; }
        public DateTime DateCreate { get; set; }
        public string Fio { get; set; }

        public Product[] Products { get; set; }

        public double TotalPrice { get { return Products.Sum(item => item.TotalPrice); } }
    }
    public class Product
    {
        public string Id { get; set; }
        public string Article { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
        public int Count { get; set; }

        public double TotalPrice { get { return Price * Count; } }
    }
    #endregion

    #region Classes
    public class AuthFake : IAuth
    {
        const string CookieAuth = "authguid";
        const string CookieBasicAuth = "Authorization";

        const string AdminLogin = "admin";
        const string AdminPass = "12345";

        public bool CheckAuthBasic(HttpRequestBase request, out string cookieName, out string cookieValue)
        {
            var auth = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(auth))
            {
                var cred = ASCIIEncoding.ASCII.GetString(Convert.FromBase64String(auth.Substring(6))).Split(':');
                var user = new { Name = cred[0], Pass = cred[1] };
                if (user.Name == AdminLogin && user.Pass == AdminPass)
                {
                    cookieName = CookieAuth;
                    cookieValue = Guid.NewGuid().ToString();
                    return true;
                }
            }

            cookieName = "";
            cookieValue = "";
            return false;
        }
        public bool IsAuth(HttpRequestBase request)
        {
            var auth = request.Cookies[CookieAuth];
            return auth != null && !string.IsNullOrEmpty(auth.Value);
        }
    }
    public class OrdersFake : IOrders
    {
        public Order[] GetAll()
        {
            return new[] {
                new Order()
                {
                    IdGuid = "36f09a0a-4864-4e36-b6aa-d581f8cace02",
                    Id = "16123",
                    DateCreate = new DateTime(2018, 10, 1, 12, 4, 15),
                    Fio = "Иванов Иван Иванович",
                    
                    Products = new Product[]
                    {
                        new Product()
                        {
                            Id = "dcbfb7cf-7a66-4545-a100-c5e5c63035ab",
                            Name = "Товар 1",
                            Article = "156118",
                            Count = 10,
                            Price = 152.15d
                        },
                        new Product()
                        {
                            Id = "9353cf19-f1c3-4fa7-b0d2-bff82e80e75a",
                            Name = "Товар 2",
                            Article = "456478",
                            Count = 1,
                            Price = 9845.00d
                        }
                    }
                }
            };
        }


        public bool Update(Order[] orders)
        {
            return true;
        }
    }
    public class ProductsFake : IProducts
    {
        public bool Upload(Product[] products)
        {
            return true;
        }
    }
    #endregion
}
