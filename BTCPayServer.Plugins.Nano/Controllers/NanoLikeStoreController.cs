using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Text.Encodings.Web;

using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.Payments;
using BTCPayServer.Plugins.Nano.RPC.Models;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Plugins.Nano.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Security;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Nano.Controllers
{
    [Route("stores/{storeId}/nanolike")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UINanoLikeStoreController : Controller
    {
        private readonly NanoLikeConfiguration _NanoLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly NanoRPCProvider _NanoRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private IStringLocalizer StringLocalizer { get; }
        private readonly HtmlEncoder _html;

        public UINanoLikeStoreController(NanoLikeConfiguration nanoLikeConfiguration,
            StoreRepository storeRepository, NanoRPCProvider nanoRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            IStringLocalizer stringLocalizer, HtmlEncoder html)
        {
            _NanoLikeConfiguration = nanoLikeConfiguration;
            _StoreRepository = storeRepository;
            _NanoRpcProvider = nanoRpcProvider;
            _handlers = handlers;
            StringLocalizer = stringLocalizer;
            _html = html;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        // [HttpGet()]
        // public async Task<IActionResult> GetStoreNanoLikePaymentMethods()
        // {
        //     return View("/Views/Nano/GetStoreNanoLikePaymentMethods.cshtml", await GetVM(StoreData));
        // }
        [NonAction]
        public async Task<NanoLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            // var accountsList = _NanoLikeConfiguration.NanoLikeConfigurationItems.ToDictionary(pair => pair.Key,
            //     pair => GetAccounts(pair.Key));

            // await Task.WhenAll(accountsList.Values);
            return new NanoLikePaymentMethodListViewModel()
            {
                Items = _NanoLikeConfiguration.NanoLikeConfigurationItems.Select(pair =>
                    GetNanoLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters
                        // accountsList[pair.Key].Result
                        ))
            };
        }

        private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_NanoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary)
                // && summary.WalletAvailable
                )
                {

                    return _NanoRpcProvider.RpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
                }
            }
            catch
            {
                // ignored
            }

            return Task.FromResult<GetAccountsResponse>(null);
        }

        private NanoLikePaymentMethodViewModel GetNanoLikePaymentMethodViewModel(
            StoreData storeData, string cryptoCode,
            IPaymentFilter excludeFilters
            // GetAccountsResponse accountsResponse
            )
        {
            var nano = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is NanoPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (NanoPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = nano.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
            _NanoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _NanoLikeConfiguration.NanoLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem);
            // var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
            //     new SelectListItem(
            //         $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
            //         account.AccountIndex.ToString(CultureInfo.InvariantCulture)));

            // var settlementThresholdChoice = NanoLikeSettlementThresholdChoice.StoreSpeedPolicy;
            // if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
            // {
            //     settlementThresholdChoice = confirmations switch
            //     {
            //         0 => NanoLikeSettlementThresholdChoice.ZeroConfirmation,
            //         1 => NanoLikeSettlementThresholdChoice.AtLeastOne,
            //         10 => NanoLikeSettlementThresholdChoice.AtLeastTen,
            //         _ => NanoLikeSettlementThresholdChoice.Custom
            //     };
            // }

            return new NanoLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AddressId = settings?.AddressId,
                // Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                //     nameof(SelectListItem.Text)),
                // SettlementConfirmationThresholdChoice = settlementThresholdChoice,
                // CustomSettlementConfirmationThreshold =
                //     settings != null &&
                //     settlementThresholdChoice is NanoLikeSettlementThresholdChoice.Custom
                //         ? settings.InvoiceSettledConfirmationThreshold
                //         : null
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> WalletTransaction(string storeId, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            try
            {
                var nanoCfg = await getPaymentConfig(storeId, cryptoCode);

                if (nanoCfg.Wallet == null)
                {
                    var walletSetupVm = new NanoWalletSetupViewModel
                    {
                        StoreId = storeId,
                        CryptoCode = cryptoCode
                    };

                    return View("/Views/Nano/SetupNanoWallet.cshtml", walletSetupVm);
                }
                // use nanoCfg
            }
            catch (Exception ex)
            {
                // handle malformed/legacy data
                Console.WriteLine(ex);
            }

            // var vm = GetNanoLikePaymentMethodViewModel(StoreData, cryptoCode,
            //     StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

            var rateUsdPerNano = 7.25m;

            var vm = new NanoListTransactionsViewModel
            {
                // WalletId = walletId,
                CryptoCode = "XNO",
                Page = 1,
                PageSize = 50,
                CurrentBalanceNano = 13.234m
            };

            vm.Labels.Add(("Deposit", "#E3F2FD", "#0D47A1"));
            vm.Labels.Add(("Withdrawal", "#FCE4EC", "#AD1457"));
            vm.Labels.Add(("Invoice", "#E8F5E9", "#1B5E20"));

            vm.Transactions.Add(MakeTx(
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            confirmed: true,
            positive: true,
            nanoAmount: 1.234m,
            address: "nano_3receiveaddress11111111111111111111111111111111111111111",
            comment: "Payment for Invoice #1234",
            tags: new[] { "Invoice", "customer:ALPHA" },
            rate: rateUsdPerNano));

            vm.Total = vm.Transactions.Count;

            return View("/Views/Nano/NanoWalletTransactions.cshtml", vm);
        }

        private static NanoListTransactionsViewModel.TransactionViewModel MakeTx(
        DateTimeOffset timestamp,
        bool confirmed,
        bool positive,
        decimal nanoAmount,
        string address,
        string comment,
        IEnumerable<string> tags,
        decimal? rate)
        {
            var id = "MOCK_" + Guid.NewGuid().ToString("N").Substring(0, 16);
            var signedAmount = positive ? nanoAmount : -nanoAmount;
            var fiat = rate.HasValue ? signedAmount * rate.Value : (decimal?)null;

            return new NanoListTransactionsViewModel.TransactionViewModel
            {
                Timestamp = timestamp,
                IsConfirmed = confirmed,
                Positive = positive,
                Comment = comment,
                Id = id,
                Link = $"https://nanolooker.com/block/{id}",
                Address = address,
                Amount = FormatNanoAmount(signedAmount),
                Tags = new List<string>(tags),
                Rate = rate,
                FiatAmount = fiat,
                FiatCode = "USD"
            };
        }

        private static string FormatNanoAmount(decimal signedAmount)
        {
            var sign = signedAmount >= 0 ? "+" : "-";
            var abs = Math.Abs(signedAmount);
            return $"{sign}{abs.ToString("N6", CultureInfo.InvariantCulture)} NANO";
        }

        // [HttpPost("{cryptoCode}")]
        // [DisableRequestSizeLimit]
        // public async Task<IActionResult> GetStoreNanoLikePaymentMethod(NanoLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        // {
        //     cryptoCode = cryptoCode.ToUpperInvariant();
        //     if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.TryGetValue(cryptoCode,
        //         out var configurationItem))
        //     {
        //         return NotFound();
        //     }

        //     if (command == "add-account")
        //     {
        //         try
        //         {
        //             // var newAccount = await _NanoRpcProvider.RpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest()
        //             // {
        //             //     Label = viewModel.NewAccountLabel
        //             // });
        //             // viewModel.AccountIndex = newAccount.AccountIndex;
        //         }
        //         catch (Exception)
        //         {
        //             ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not create a new account."]);
        //         }

        //     }
        //     else if (command == "upload-wallet")
        //     {
        //         var valid = true;
        //         if (viewModel.WalletFile == null)
        //         {
        //             ModelState.AddModelError(nameof(viewModel.WalletFile), StringLocalizer["Please select the view-only wallet file"]);
        //             valid = false;
        //         }
        //         if (viewModel.WalletKeysFile == null)
        //         {
        //             ModelState.AddModelError(nameof(viewModel.WalletKeysFile), StringLocalizer["Please select the view-only wallet keys file"]);
        //             valid = false;
        //         }
        //         // if (configurationItem.WalletDirectory == null)
        //         // {
        //         //     ModelState.AddModelError(nameof(viewModel.WalletFile), StringLocalizer["This installation doesn't support wallet import (BTCPAY_XMR_WALLET_DAEMON_WALLETDIR is not set)"]);
        //         //     valid = false;
        //         // }
        //         if (valid)
        //         {
        //             // if (_NanoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
        //             // {
        //             //     if (summary.WalletAvailable)
        //             //     {
        //             //         TempData.SetStatusMessageModel(new StatusMessageModel
        //             //         {
        //             //             Severity = StatusMessageModel.StatusSeverity.Error,
        //             //             Message = StringLocalizer["There is already an active wallet configured for {0}. Replacing it would break any existing invoices!", cryptoCode].Value
        //             //         });
        //             //         return RedirectToAction(nameof(GetStoreNanoLikePaymentMethod),
        //             //             new { cryptoCode });
        //             //     }
        //             // }

        //             // var fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet");
        //             // using (var fileStream = new FileStream(fileAddress, FileMode.Create))
        //             // {
        //             //     await viewModel.WalletFile.CopyToAsync(fileStream);
        //             //     try
        //             //     {
        //             //         Exec($"chmod 666 {fileAddress}");
        //             //     }
        //             //     catch
        //             //     {
        //             //         // ignored
        //             //     }
        //             // }

        //             // fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet.keys");
        //             // using (var fileStream = new FileStream(fileAddress, FileMode.Create))
        //             // {
        //             //     await viewModel.WalletKeysFile.CopyToAsync(fileStream);
        //             //     try
        //             //     {
        //             //         Exec($"chmod 666 {fileAddress}");
        //             //     }
        //             //     catch
        //             //     {
        //             //         // ignored
        //             //     }
        //             // }

        //             // fileAddress = Path.Combine(configurationItem.WalletDirectory, "password");
        //             // using (var fileStream = new StreamWriter(fileAddress, false))
        //             // {
        //             //     await fileStream.WriteAsync(viewModel.WalletPassword);
        //             //     try
        //             //     {
        //             //         Exec($"chmod 666 {fileAddress}");
        //             //     }
        //             //     catch
        //             //     {
        //             //         // ignored
        //             //     }
        //             // }

        //             try
        //             {
        //                 var response = await _NanoRpcProvider.RpcClients[cryptoCode].SendCommandAsync<OpenWalletRequest, OpenWalletResponse>("open_wallet", new OpenWalletRequest
        //                 {
        //                     Filename = "wallet",
        //                     Password = viewModel.WalletPassword
        //                 });
        //                 if (response?.Error != null)
        //                 {
        //                     throw new WalletOpenException(response.Error.Message);
        //                 }
        //             }
        //             catch (Exception ex)
        //             {
        //                 ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not open the wallet: {0}", ex.Message]);
        //                 return View("/Views/Nano/GetStoreNanoLikePaymentMethod.cshtml", viewModel);
        //             }

        //             TempData.SetStatusMessageModel(new StatusMessageModel
        //             {
        //                 Severity = StatusMessageModel.StatusSeverity.Info,
        //                 Message = StringLocalizer["View-only wallet files uploaded. The wallet will soon become available."].Value
        //             });
        //             return RedirectToAction(nameof(GetStoreNanoLikePaymentMethod), new { cryptoCode });
        //         }
        //     }

        //     if (!ModelState.IsValid)
        //     {

        //         var vm = GetNanoLikePaymentMethodViewModel(StoreData, cryptoCode,
        //             StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

        //         vm.Enabled = viewModel.Enabled;
        //         vm.NewAccountLabel = viewModel.NewAccountLabel;
        //         vm.AccountIndex = viewModel.AccountIndex;
        //         vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
        //         vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
        //         // vm.SupportWalletExport = configurationItem.WalletDirectory is not null;
        //         vm.SupportWalletExport = false;
        //         return View("/Views/Nano/GetStoreNanoLikePaymentMethod.cshtml", vm);
        //     }

        //     var storeData = StoreData;
        //     var blob = storeData.GetStoreBlob();
        //     Console.WriteLine("ABCD Handlers " + _handlers);
        //     storeData.SetPaymentMethodConfig(_handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)], new NanoPaymentPromptDetails()
        //     {
        //         AccountIndex = viewModel.AccountIndex,
        //         InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
        //         {
        //             NanoLikeSettlementThresholdChoice.ZeroConfirmation => 0,
        //             NanoLikeSettlementThresholdChoice.AtLeastOne => 1,
        //             NanoLikeSettlementThresholdChoice.AtLeastTen => 10,
        //             NanoLikeSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
        //             _ => null
        //         }
        //     });

        //     blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
        //     storeData.SetStoreBlob(blob);
        //     await _StoreRepository.UpdateStore(storeData);
        //     return RedirectToAction("GetStoreNanoLikePaymentMethods",
        //         new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        // }

        [HttpGet("{cryptoCode}/walletsend")]
        public async Task<IActionResult> WalletSend(
            // [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            string cryptoCode, string storeId,
            string? defaultDestination = null, string? defaultAmount = null, string[]? bip21 = null,
            [FromQuery] string? returnUrl = null)
        {
            var vm = new NanoWalletSendModel
            {
                CryptoCode = string.IsNullOrWhiteSpace(cryptoCode) ? "XNO" : cryptoCode.ToUpperInvariant(),
                CurrentBalance = 1234,
                CryptoDivisibility = 6,
                FiatDivisibility = 2,
                Fiat = "USD",
                Rate = 7.25m, // mock FX rate
                Outputs = new List<NanoWalletSendModel.TransactionOutput>
            {
                new NanoWalletSendModel.TransactionOutput
                {
                    DestinationAddress = "nano_3mockaddress1exampleexampleexampleexampleexample1",
                    Amount = 1.234m,
                    PayoutId = "mock-payout-001",
                    Labels = new[] { "demo", "test" }
                }
            },
                BackUrl = Url.Action(nameof(WalletTransaction), new
                {
                    storeId,
                    cryptoCode
                }),
                // ReturnUrl = Url.Action(nameof(WalletSend), new { storeId, cryptoCode })
            };


            return View("/Views/Nano/NanoWalletSend.cshtml", vm);
        }

        [HttpGet("{cryptoCode}/walletreceive")]
        public IActionResult WalletReceive(string storeId, string cryptoCode)
        {
            // Your view reads Context.GetRouteValue("walletId").ToString();
            // Ensure it's present to avoid a null.ToString() crash.
            // if (!RouteData.Values.ContainsKey("walletId"))
            //     RouteData.Values["walletId"] = "mockwallet";

            var vm = new BTCPayServer.Plugins.Nano.ViewModels.NanoWalletReceiveViewModel
            {
                CryptoCode = string.IsNullOrWhiteSpace(cryptoCode) ? "XNO" : cryptoCode.ToUpperInvariant(),
                Address = null,      // null => shows the "Generate..." button
                PaymentLink = null,
                // Leave ReturnUrl null; your view computes it via Url.Action(...)
                CryptoImage = Url.Content("/_content/BTCPayServer.Plugins.Nano/resources/img/screengrab.png") // optional; remove if not available
            };

            // If your view file name is WalletReceive.cshtml under Views/UINanoLikeStore, this is fine:
            // return View(vm);
            // Otherwise: return View("~/Views/Nano/NanoWalletReceive.cshtml", vm);
            return View("/Views/Nano/NanoWalletReceive.cshtml", vm);

        }

        [HttpPost("{cryptoCode}/walletreceive")]
        [ValidateAntiForgeryToken]
        public IActionResult WalletReceive(string storeId, string cryptoCode, [FromForm] string command, [FromForm] NanoWalletReceiveViewModel vm)
        {
            // if (!RouteData.Values.ContainsKey("walletId"))
            //     RouteData.Values["walletId"] = "mockwallet";
            vm ??= new BTCPayServer.Plugins.Nano.ViewModels.NanoWalletReceiveViewModel();
            vm.CryptoCode = string.IsNullOrWhiteSpace(vm.CryptoCode)
                ? (string.IsNullOrWhiteSpace(cryptoCode) ? "XNO" : cryptoCode.ToUpperInvariant())
                : vm.CryptoCode;

            vm.CryptoImage ??= Url.Content("/_content/BTCPayServer.Plugins.Nano/resources/img/screengrab.png"); // optional

            if (string.Equals(command, "generate-new-address", StringComparison.OrdinalIgnoreCase))
            {
                // Mock address and link
                var mockAddress = "nano_3mockaddress1x9o7e9q7wz4y7p6r5s4t3u2v1w0x9y8z7a6b5c4d3e2f1";
                vm.Address = mockAddress;
                vm.PaymentLink = $"{vm.CryptoCode.ToLowerInvariant()}:{mockAddress}";
            }

            // return View(vm);
            // Or: return View("~/Views/Nano/NanoWalletReceive.cshtml", vm);
            return View("/Views/Nano/NanoWalletReceive.cshtml", vm);
        }

        [HttpGet("{cryptoCode}/walletsettings")]
        public async Task<IActionResult> WalletSettings(string storeId, string cryptoCode)
        {
            bool enabled = false;
            string address = "";

            try
            {
                NanoLikePaymentMethodConfiguration config = await getPaymentConfig(storeId, cryptoCode);

                enabled = config.Enabled;
                address = config.PublicAddress;
            }
            catch (Exception e)
            {
                enabled = false;
            }

            string code = "NANO";

            var vm = new NanoWalletSettingsViewModel
            {
                StoreId = storeId,
                // StoreName = "Demo Store",
                CryptoCode = "XNO",
                UriScheme = "xno",
                // WalletId = $"{storeId}-{code}",

                Enabled = enabled,
                // PayJoinEnabled = false,
                // CanUsePayJoin = false,
                // CanSetupMultiSig = false,
                // IsMultiSigOnServer = false,
                // DefaultIncludeNonWitnessUtxo = false,
                // NBXSeedAvailable = false,

                Label = $"{code} Wallet",
                PublicAddress = address
                // DerivationScheme = $"{code}_MOCK_DERIVATION",
                // DerivationSchemeInput = null
            };

            // // Ensure list is non-null so the view’s for-loop and JSON serialization don’t NRE
            // vm.AccountKeys ??= new();

            // // Text used by the modal buttons in the view
            // ViewData["ReplaceDescription"] = $"This will disconnect the current {code} wallet from the store and start a new setup.";
            // ViewData["RemoveDescription"] = $"This will remove the {code} wallet from the store. You can add one again later.";

            return View("/Views/Nano/NanoWalletSettings.cshtml", vm); // or just: return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateEnabled(string storeId, string cryptoCode, bool enabled)
        {
            NanoLikePaymentMethodConfiguration config = await getPaymentConfig(storeId, cryptoCode);

            config.Enabled = enabled;

            await setPaymentConfig(storeId, cryptoCode, config);

            return RedirectToAction(nameof(WalletSettings), new { storeId, cryptoCode });
        }

        [HttpGet("onchain/{cryptoCode}/delete")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public ActionResult DeleteWallet(string storeId, string cryptoCode)
        {
            // Check wallet ID is present in settings
            // If not present, break

            return View("Confirm", new ConfirmModel
            {
                Title = StringLocalizer["Remove Nano wallet"],
                Description = WalletRemoveWarning(true),
                DescriptionHtml = true,
                Action = StringLocalizer["Remove"]
            });
        }

        [HttpPost("onchain/{cryptoCode}/delete")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> ConfirmDeleteWallet(string storeId, string cryptoCode)
        {
            NanoLikePaymentMethodConfiguration config = await getPaymentConfig(storeId, cryptoCode);
            Console.WriteLine("HERE");
            Console.WriteLine(storeId);
            Console.WriteLine(config.Wallet);
            Console.WriteLine(config.Enabled);
            if (config.Wallet == null) return Redirect("/");

            await setPaymentConfig(storeId, cryptoCode, new NanoLikePaymentMethodConfiguration
            {
                Wallet = null,
                Enabled = false
            });

            WalletDestroyResponse response;

            try
            {
                response = await _NanoRpcProvider.RpcClients[cryptoCode].SendCommandAsync<WalletDestroyRequest, WalletDestroyResponse>("wallet_destroy", new WalletDestroyRequest
                {
                    Wallet = config.Wallet
                });

                TempData[WellKnownTempData.SuccessMessage] =
                $"On-Chain payment for Nano has been removed.";
            }
            catch (Exception e)
            {
                Console.WriteLine(e);

                TempData[WellKnownTempData.ErrorMessage] =
                $"Failed to delete nano wallet. Please remove it manually from the node.";
            }

            return Redirect("/");
        }

        [HttpGet("onchain/{cryptoCode}/generateWallet")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> GenerateWallet(string storeId, string cryptoCode)
        {
            try
            {
                WalletCreateResponse response = await _NanoRpcProvider.RpcClients[cryptoCode].SendCommandAsync<object, WalletCreateResponse>("wallet_create", null);

                string wallet = response.Wallet;

                CreateAccountResponse accountResponse = await _NanoRpcProvider.RpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("account_create", new CreateAccountRequest
                {
                    Wallet = wallet
                });

                string address = accountResponse.Account;

                NanoLikePaymentMethodConfiguration newConfig = new NanoLikePaymentMethodConfiguration
                {
                    Enabled = true,
                    Wallet = wallet,
                    PublicAddress = address
                };

                await setPaymentConfig(storeId, cryptoCode, newConfig);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                TempData[WellKnownTempData.ErrorMessage] =
                $"Generation of Nano wallet failed. Please try again later.";

                return Redirect("/");
            }

            TempData[WellKnownTempData.SuccessMessage] = $"On-Chain payment for Nano has been created.";
            return RedirectToAction(nameof(WalletSettings), new { storeId, cryptoCode });
        }

        public async Task<NanoLikePaymentMethodConfiguration> getPaymentConfig(string storeId, string cryptoCode)
        {
            var code = string.IsNullOrWhiteSpace(cryptoCode) ? "XNO" : cryptoCode.ToUpperInvariant();
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(code);
            var store = await _StoreRepository.FindStore(storeId);
            var cfg = store.GetPaymentMethodConfigs();

            if (cfg.TryGetValue(pmi, out var token) && token is JToken obj)
            {
                try
                {
                    var nanoCfg = obj.ToObject<NanoLikePaymentMethodConfiguration>();

                    return nanoCfg;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);

                    return new NanoLikePaymentMethodConfiguration
                    {
                        Wallet = null,
                        Enabled = false
                    };
                }
            }
            else
            {
                return new NanoLikePaymentMethodConfiguration
                {
                    Wallet = null,
                    Enabled = false
                };
            }
        }

        public async Task setPaymentConfig(string storeId, string cryptoCode, NanoLikePaymentMethodConfiguration newConfig)
        {
            var store = await _StoreRepository.FindStore(storeId);

            if (store is null) throw new Exception("No Store Found");

            var code = string.IsNullOrWhiteSpace(cryptoCode) ? "XNO" : cryptoCode.ToUpperInvariant();
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(code);

            if (newConfig == null)
            {
                Console.WriteLine("HERE SETTING CONFIG TO NULL");
                store.SetPaymentMethodConfig(pmi, null);
                return;
            }

            JObject jsonConfig = JObject.FromObject(newConfig);

            store.SetPaymentMethodConfig(pmi, jsonConfig);

            await _StoreRepository.UpdateStore(store);
        }

        private string WalletRemoveWarning(bool isHotWallet)
        {
            return WalletWarning(isHotWallet,
                $"The store won't be able to receive Nano onchain payments until a new wallet is set up.");
        }

        private string WalletWarning(bool isHotWallet, string info)
        {
            var walletType = isHotWallet ? "hot" : "watch-only";
            var additionalText = isHotWallet
                ? ""
                : " or imported it into an external wallet. If you no longer have access to your private key (recovery seed), immediately replace the wallet";
            return
                $"<p class=\"text-danger fw-bold\">Please note that this is a <strong>{_html.Encode(walletType)} wallet</strong>!</p>" +
                $"<p class=\"text-danger fw-bold\">Do not proceed if you have not backed up the wallet{_html.Encode(additionalText)}.</p>" +
                $"<p class=\"text-start mb-0\">This action will erase the current wallet data from the server. {_html.Encode(info)}</p>";
        }

        private void Exec(string cmd)
        {

            var escapedArgs = cmd.Replace("\"", "\\\"", StringComparison.InvariantCulture);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "/bin/sh",
                    Arguments = $"-c \"{escapedArgs}\""
                }
            };

#pragma warning disable CA1416 // Validate platform compatibility
            process.Start();
#pragma warning restore CA1416 // Validate platform compatibility
            process.WaitForExit();
        }

        public class NanoLikePaymentMethodListViewModel
        {
            public IEnumerable<NanoLikePaymentMethodViewModel> Items { get; set; }
        }

        public class NanoLikePaymentMethodViewModel : IValidatableObject
        {
            public NanoRPCProvider.NanoLikeSummary Summary { get; set; }
            public bool SupportWalletExport { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public string AddressId { get; set; }
            public bool Enabled { get; set; }

            // public IEnumerable<SelectListItem> Accounts { get; set; }
            public bool WalletFileFound { get; set; }
            [Display(Name = "View-Only Wallet File")]
            public IFormFile WalletFile { get; set; }
            [Display(Name = "Wallet Keys File")]
            public IFormFile WalletKeysFile { get; set; }
            [Display(Name = "Wallet Password")]
            public string WalletPassword { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public NanoLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is NanoLikeSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        new[] { nameof(CustomSettlementConfirmationThreshold) });
                }
            }
        }

        public enum NanoLikeSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,
            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,
            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,
            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,
            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}