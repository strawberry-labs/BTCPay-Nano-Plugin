using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

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

        public UINanoLikeStoreController(NanoLikeConfiguration nanoLikeConfiguration,
            StoreRepository storeRepository, NanoRPCProvider nanoRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            IStringLocalizer stringLocalizer)
        {
            _NanoLikeConfiguration = nanoLikeConfiguration;
            _StoreRepository = storeRepository;
            _NanoRpcProvider = nanoRpcProvider;
            _handlers = handlers;
            StringLocalizer = stringLocalizer;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreNanoLikePaymentMethods()
        {
            return View("/Views/Nano/GetStoreNanoLikePaymentMethods.cshtml", await GetVM(StoreData));
        }
        [NonAction]
        public async Task<NanoLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _NanoLikeConfiguration.NanoLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new NanoLikePaymentMethodListViewModel()
            {
                Items = _NanoLikeConfiguration.NanoLikeConfigurationItems.Select(pair =>
                    GetNanoLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            };
        }

        private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_NanoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {

                    return _NanoRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
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
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var nano = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is NanoPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (NanoPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = nano.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
            _NanoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _NanoLikeConfiguration.NanoLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem);
            var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
                new SelectListItem(
                    $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
                    account.AccountIndex.ToString(CultureInfo.InvariantCulture)));

            var settlementThresholdChoice = NanoLikeSettlementThresholdChoice.StoreSpeedPolicy;
            if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
            {
                settlementThresholdChoice = confirmations switch
                {
                    0 => NanoLikeSettlementThresholdChoice.ZeroConfirmation,
                    1 => NanoLikeSettlementThresholdChoice.AtLeastOne,
                    10 => NanoLikeSettlementThresholdChoice.AtLeastTen,
                    _ => NanoLikeSettlementThresholdChoice.Custom
                };
            }

            return new NanoLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = settings?.AccountIndex ?? accountsResponse?.SubaddressAccounts?.FirstOrDefault()?.AccountIndex ?? 0,
                Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text)),
                SettlementConfirmationThresholdChoice = settlementThresholdChoice,
                CustomSettlementConfirmationThreshold =
                    settings != null &&
                    settlementThresholdChoice is NanoLikeSettlementThresholdChoice.Custom
                        ? settings.InvoiceSettledConfirmationThreshold
                        : null
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreNanoLikePaymentMethod(string cryptoCode)
        {
            Console.WriteLine("ABCD HERE " + cryptoCode);
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                Console.WriteLine("ABCD HERE Errored");
                Console.WriteLine(_NanoLikeConfiguration.NanoLikeConfigurationItems);
                return NotFound();
            }

            var vm = GetNanoLikePaymentMethodViewModel(StoreData, cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));
            return View("/Views/Nano/GetStoreNanoLikePaymentMethod.cshtml", vm);
        }

        [HttpPost("{cryptoCode}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> GetStoreNanoLikePaymentMethod(NanoLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    var newAccount = await _NanoRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest()
                    {
                        Label = viewModel.NewAccountLabel
                    });
                    viewModel.AccountIndex = newAccount.AccountIndex;
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not create a new account."]);
                }

            }
            else if (command == "upload-wallet")
            {
                var valid = true;
                if (viewModel.WalletFile == null)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletFile), StringLocalizer["Please select the view-only wallet file"]);
                    valid = false;
                }
                if (viewModel.WalletKeysFile == null)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletKeysFile), StringLocalizer["Please select the view-only wallet keys file"]);
                    valid = false;
                }
                if (configurationItem.WalletDirectory == null)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletFile), StringLocalizer["This installation doesn't support wallet import (BTCPAY_XMR_WALLET_DAEMON_WALLETDIR is not set)"]);
                    valid = false;
                }
                if (valid)
                {
                    if (_NanoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
                    {
                        if (summary.WalletAvailable)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel
                            {
                                Severity = StatusMessageModel.StatusSeverity.Error,
                                Message = StringLocalizer["There is already an active wallet configured for {0}. Replacing it would break any existing invoices!", cryptoCode].Value
                            });
                            return RedirectToAction(nameof(GetStoreNanoLikePaymentMethod),
                                new { cryptoCode });
                        }
                    }

                    var fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet");
                    using (var fileStream = new FileStream(fileAddress, FileMode.Create))
                    {
                        await viewModel.WalletFile.CopyToAsync(fileStream);
                        try
                        {
                            Exec($"chmod 666 {fileAddress}");
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet.keys");
                    using (var fileStream = new FileStream(fileAddress, FileMode.Create))
                    {
                        await viewModel.WalletKeysFile.CopyToAsync(fileStream);
                        try
                        {
                            Exec($"chmod 666 {fileAddress}");
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    fileAddress = Path.Combine(configurationItem.WalletDirectory, "password");
                    using (var fileStream = new StreamWriter(fileAddress, false))
                    {
                        await fileStream.WriteAsync(viewModel.WalletPassword);
                        try
                        {
                            Exec($"chmod 666 {fileAddress}");
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    try
                    {
                        var response = await _NanoRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<OpenWalletRequest, OpenWalletResponse>("open_wallet", new OpenWalletRequest
                        {
                            Filename = "wallet",
                            Password = viewModel.WalletPassword
                        });
                        if (response?.Error != null)
                        {
                            throw new WalletOpenException(response.Error.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not open the wallet: {0}", ex.Message]);
                        return View("/Views/Nano/GetStoreNanoLikePaymentMethod.cshtml", viewModel);
                    }

                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Info,
                        Message = StringLocalizer["View-only wallet files uploaded. The wallet will soon become available."].Value
                    });
                    return RedirectToAction(nameof(GetStoreNanoLikePaymentMethod), new { cryptoCode });
                }
            }

            if (!ModelState.IsValid)
            {

                var vm = GetNanoLikePaymentMethodViewModel(StoreData, cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                vm.SupportWalletExport = configurationItem.WalletDirectory is not null;
                return View("/Views/Nano/GetStoreNanoLikePaymentMethod.cshtml", vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            Console.WriteLine("ABCD Handlers " + _handlers);
            storeData.SetPaymentMethodConfig(_handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)], new NanoPaymentPromptDetails()
            {
                AccountIndex = viewModel.AccountIndex,
                InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
                {
                    NanoLikeSettlementThresholdChoice.ZeroConfirmation => 0,
                    NanoLikeSettlementThresholdChoice.AtLeastOne => 1,
                    NanoLikeSettlementThresholdChoice.AtLeastTen => 10,
                    NanoLikeSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
                    _ => null
                }
            });

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreNanoLikePaymentMethods",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
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
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }

            public IEnumerable<SelectListItem> Accounts { get; set; }
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