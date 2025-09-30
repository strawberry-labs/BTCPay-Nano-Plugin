// Event model your confirmations service should publish
// Adjust/relocate if you already defined this elsewhere
using System.Collections.Generic;
using System;

namespace BTCPayServer.Plugins.Nano.RPC
{
    public class AdhocAddress : IEquatable<AdhocAddress>
    {
        public string Address { get; set; }
        public string StoreId { get; set; }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            AdhocAddress objAsPart = obj as AdhocAddress;
            if (objAsPart == null) return false;
            else return Equals(objAsPart);
        }
        // public override int GetHashCode()
        // {
        //     return Address;
        // }
        public bool Equals(AdhocAddress other)
        {
            if (other == null) return false;

            return (this.Address.Equals(other.Address));
        }
    }
}