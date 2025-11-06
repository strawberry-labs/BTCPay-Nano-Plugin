using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Plugins.Nano.Data;

namespace BTCPayServer.Plugins.Nano.Repositories;

public class InvoiceAdhocAddressRepository
{
    private readonly MyPluginDbContext _db;
    public InvoiceAdhocAddressRepository(MyPluginDbContext db) => _db = db;

    public Task<InvoiceAdhocAddress> GetAsyncById(string id, CancellationToken ct = default)
    => _db.InvoiceAdhocAddress.AsNoTracking().FirstOrDefaultAsync(x => x.id == id, ct);

    public Task<InvoiceAdhocAddress> GetAsyncByInvoice(string invoiceId, CancellationToken ct = default)
    => _db.InvoiceAdhocAddress.AsNoTracking().FirstOrDefaultAsync(x => x.invoiceId == invoiceId, ct);

    public Task<InvoiceAdhocAddress> GetAsyncByAccount(string account, CancellationToken ct = default)
    => _db.InvoiceAdhocAddress.AsNoTracking().FirstOrDefaultAsync(x => x.account == account, ct);

    public async Task<InvoiceAdhocAddress> AddAsync(string publicAddress, string privateAddress, string account, string invoiceId, CancellationToken ct = default)
    {
        var item = new InvoiceAdhocAddress { publicAddress = publicAddress, privateAddress = privateAddress, account = account, invoiceId = invoiceId };
        _db.InvoiceAdhocAddress.Add(item);
        await _db.SaveChangesAsync(ct);
        return item;
    }

    // public async Task<bool> UpdateNameAsync(string storeId, string id, string newName, CancellationToken ct = default)
    // {
    //     var entity = await _db.Items.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct);
    //     if (entity is null) return false;
    //     entity.Name = newName;
    //     entity.UpdatedAt = DateTimeOffset.UtcNow;
    //     await _db.SaveChangesAsync(ct);
    //     return true;
    // }

    // public Task<int> DeleteForStoreAsync(string storeId, CancellationToken ct = default)
    // => _db.Items.Where(x => x.StoreId == storeId).ExecuteDeleteAsync(ct); // EF Core 7+
}