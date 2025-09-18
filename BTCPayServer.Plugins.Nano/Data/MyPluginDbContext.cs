using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTCPayServer.Plugins.Nano.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BTCPayServer.Plugins.Nano;

public class MyPluginDbContext : DbContext
{
    private readonly bool _designTime;

    public MyPluginDbContext(DbContextOptions<MyPluginDbContext> options, bool designTime = false)
        : base(options)
    {
        _designTime = designTime;
    }

    // public DbSet<PluginData> PluginRecords { get; set; }
    // public DbSet<TestData> TableTest { get; set; }
    // public DbSet<TestData1> TableTest2 { get; set; }
    public DbSet<InvoiceAdhocAddress> InvoiceAdhocAddress { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.Plugins.Nano");
    }
}
