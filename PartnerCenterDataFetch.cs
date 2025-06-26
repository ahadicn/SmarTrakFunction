//using System;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Text.Json;
//using System.Threading.Tasks;
//using Microsoft.Azure.WebJobs;
//using Microsoft.Extensions.Logging;
//using Microsoft.Data.SqlClient;
//using Microsoft.Identity.Client;
//using System.Collections.Generic;
//using Microsoft.Azure.Functions.Worker;

//namespace PartnerCenterDataFetch
//{
//    public class PartnerCenterDataFetch
//    {
//        private readonly HttpClient _httpClient;
//        private readonly string _tenantId;
//        private readonly string _clientId;
//        private readonly string _clientSecret;
//        private readonly string _connectionString;
//        private readonly string _baseUrl = "https://api.partnercenter.microsoft.com/v1";

//        public PartnerCenterDataFetch(IHttpClientFactory httpClientFactory)
//        {
//            _httpClient = httpClientFactory.CreateClient();
//            _tenantId = Environment.GetEnvironmentVariable("TenantIdExternal");
//            _clientId = Environment.GetEnvironmentVariable("ClientIdExternal");
//            _clientSecret = Environment.GetEnvironmentVariable("ClientSecretExternal");
//            _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
//        }

//        [FunctionName("FetchPartnerCenterData")]
//        public async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo myTimer, ILogger log) // Runs daily at midnight
//        {
//            log.LogInformation($"Partner Center data fetch started at: {DateTime.Now}");

//            // Step 1: Get access token using client credentials flow
//            string accessToken = await GetAccessToken(log);
//            if (string.IsNullOrEmpty(accessToken))
//            {
//                log.LogError("Failed to obtain access token.");
//                return;
//            }

//            // Step 2: Fetch and store customers
//            var customers = await GetCustomers(accessToken, log);
//            foreach (var customer in customers)
//            {
//                await StoreCustomer(customer, log);
//                // Step 3: Fetch and store subscriptions for each customer
//                var subscriptions = await GetCustomerSubscriptions(customer.Id, accessToken, log);
//                foreach (var subscription in subscriptions)
//                {
//                    await StoreSubscription(subscription, customer.Id, log);
//                }
//            }

//            log.LogInformation("Partner Center data fetch completed.");
//        }

//        private async Task<string> GetAccessToken(ILogger log)
//        {
//            try
//            {
//                var app = ConfidentialClientApplicationBuilder
//                    .Create(_clientId)
//                    .WithClientSecret(_clientSecret)
//                    .WithAuthority(new Uri($"https://login.microsoftonline.com/{_tenantId}"))
//                    .Build();

//                var scopes = new[] { "https://api.partnercenter.microsoft.com/.default" };
//                var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();
//                return result.AccessToken;
//            }
//            catch (Exception ex)
//            {
//                log.LogError($"Error obtaining access token: {ex.Message}");
//                return null;
//            }
//        }

//        private async Task<List<Customer>> GetCustomers(string accessToken, ILogger log)
//        {
//            var customers = new List<Customer>();
//            try
//            {
//                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//                var response = await _httpClient.GetAsync($"{_baseUrl}/customers");
//                response.EnsureSuccessStatusCode();
//                var content = await response.Content.ReadAsStringAsync();
//                var jsonDoc = JsonDocument.Parse(content);
//                var items = jsonDoc.RootElement.GetProperty("items").EnumerateArray();

//                foreach (var item in items)
//                {
//                    customers.Add(new Customer
//                    {
//                        Id = item.GetProperty("id").GetString(),
//                        Name = item.GetProperty("companyProfile").GetProperty("companyName").GetString(),
//                        Domain = item.GetProperty("companyProfile").GetProperty("domain").GetString()
//                    });
//                }
//            }
//            catch (Exception ex)
//            {
//                log.LogError($"Error fetching customers: {ex.Message}");
//            }
//            return customers;
//        }

//        private async Task<List<Subscription>> GetCustomerSubscriptions(string customerId, string accessToken, ILogger log)
//        {
//            var subscriptions = new List<Subscription>();
//            try
//            {
//                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//                var response = await _httpClient.GetAsync($"{_baseUrl}/customers/{customerId}/subscriptions");
//                response.EnsureSuccessStatusCode();
//                var content = await response.Content.ReadAsStringAsync();
//                var jsonDoc = JsonDocument.Parse(content);
//                var items = jsonDoc.RootElement.GetProperty("items").EnumerateArray();

//                foreach (var item in items)
//                {
//                    subscriptions.Add(new Subscription
//                    {
//                        Id = item.GetProperty("id").GetString(),
//                        OfferName = item.GetProperty("offerName").GetString(),
//                        Status = item.GetProperty("status").GetString(),
//                        Quantity = item.GetProperty("quantity").GetInt32(),
//                        UnitType = item.GetProperty("unitType").GetString(),
//                        BillingCycle = item.GetProperty("billingCycle").GetString(),
//                        BillingType = item.GetProperty("billingType").GetString(),
//                        CreatedDate = item.TryGetProperty("creationDate", out var created) ? DateTime.Parse(created.GetString()) : null,
//                        StartedDate = item.TryGetProperty("effectiveStartDate", out var started) ? DateTime.Parse(started.GetString()) : null
//                    });
//                }
//            }
//            catch (Exception ex)
//            {
//                log.LogError($"Error fetching subscriptions for customer {customerId}: {ex.Message}");
//            }
//            return subscriptions;
//        }

//        private async Task StoreCustomer(Customer customer, ILogger log)
//        {
//            try
//            {
//                using var conn = new SqlConnection(_connectionString);
//                await conn.OpenAsync();
//                var cmd = new SqlCommand(@"
//                    MERGE INTO Customer AS target
//                    USING (SELECT @Id, @Name, @Domain) AS source (Id, Name, Domain)
//                    ON target.Id = source.Id
//                    WHEN MATCHED THEN
//                        UPDATE SET Name = source.Name, Domain = source.Domain, UpdatedAt = CURRENT_TIMESTAMP
//                    WHEN NOT MATCHED THEN
//                        INSERT (Id, Name, Domain, CreatedAt, UpdatedAt)
//                        VALUES (source.Id, source.Name, source.Domain, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);", conn);
//                cmd.Parameters.AddWithValue("@Id", customer.Id);
//                cmd.Parameters.AddWithValue("@Name", customer.Name);
//                cmd.Parameters.AddWithValue("@Domain", customer.Domain ?? (object)DBNull.Value);
//                await cmd.ExecuteNonQueryAsync();
//                log.LogInformation($"Stored customer: {customer.Name}");
//            }
//            catch (Exception ex)
//            {
//                log.LogError($"Error storing customer {customer.Id}: {ex.Message}");
//            }
//        }

//        private async Task StoreSubscription(Subscription subscription, string customerId, ILogger log)
//        {
//            try
//            {
//                using var conn = new SqlConnection(_connectionString);
//                await conn.OpenAsync();
//                var cmd = new SqlCommand(@"
//                    MERGE INTO Subscription AS target
//                    USING (SELECT @Id, @CustomerId, @OfferName, @Status, @Quantity, @UnitType, @BillingCycle, @BillingType, @CreatedDate, @StartedDate) 
//                          AS source (Id, CustomerId, OfferName, Status, Quantity, UnitType, BillingCycle, BillingType, CreatedDate, StartedDate)
//                    ON target.Id = source.Id
//                    WHEN MATCHED THEN
//                        UPDATE SET 
//                            CustomerId = source.CustomerId,
//                            OfferName = source.OfferName,
//                            Status = source.Status,
//                            Quantity = source.Quantity,
//                            UnitType = source.UnitType,
//                            BillingCycle = source.BillingCycle,
//                            BillingType = source.BillingType,
//                            CreatedDate = source.CreatedDate,
//                            StartedDate = source.StartedDate,
//                            UpdatedAt = CURRENT_TIMESTAMP
//                    WHEN NOT MATCHED THEN
//                        INSERT (Id, CustomerId, OfferName, Status, Quantity, UnitType, BillingCycle, BillingType, CreatedDate, StartedDate, CreatedAt, UpdatedAt)
//                        VALUES (source.Id, source.CustomerId, source.OfferName, source.Status, source.Quantity, source.UnitType, source.BillingCycle, source.BillingType, source.CreatedDate, source.StartedDate, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);", conn);
//                cmd.Parameters.AddWithValue("@Id", subscription.Id);
//                cmd.Parameters.AddWithValue("@CustomerId", customerId);
//                cmd.Parameters.AddWithValue("@OfferName", subscription.OfferName);
//                cmd.Parameters.AddWithValue("@Status", subscription.Status);
//                cmd.Parameters.AddWithValue("@Quantity", subscription.Quantity);
//                cmd.Parameters.AddWithValue("@UnitType", subscription.UnitType ?? (object)DBNull.Value);
//                cmd.Parameters.AddWithValue("@BillingCycle", subscription.BillingCycle);
//                cmd.Parameters.AddWithValue("@BillingType", subscription.BillingType);
//                cmd.Parameters.AddWithValue("@CreatedDate", subscription.CreatedDate ?? (object)DBNull.Value);
//                cmd.Parameters.AddWithValue("@StartedDate", subscription.StartedDate ?? (object)DBNull.Value);
//                await cmd.ExecuteNonQueryAsync();
//                log.LogInformation($"Stored subscription: {subscription.Id} for customer: {customerId}");
//            }
//            catch (Exception ex)
//            {
//                log.LogError($"Error storing subscription {subscription.Id}: {ex.Message}");
//            }
//        }
//    }

//    public class Customer
//    {
//        public string Id { get; set; }
//        public string Name { get; set; }
//        public string Domain { get; set; }
//    }

//    public class Subscription
//    {
//        public string Id { get; set; }
//        public string OfferName { get; set; }
//        public string Status { get; set; }
//        public int Quantity { get; set; }
//        public string UnitType { get; set; }
//        public string BillingCycle { get; set; }
//        public string BillingType { get; set; }
//        public DateTime? CreatedDate { get; set; }
//        public DateTime? StartedDate { get; set; }
//    }
//}