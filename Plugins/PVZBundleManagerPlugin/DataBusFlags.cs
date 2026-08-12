using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PvZBundleManagerPlugin
{
    internal enum DataBusFlags
    {
        FirstFreeFlag = 0,
        NeedNetworkIdFlag = 1 << (FirstFreeFlag + 0),
        InterfaceHasConnectionsFlag = 1 << (FirstFreeFlag + 1),
        AlwaysCreateEntityBusClientFlag = 1 << (FirstFreeFlag + 2),
        AlwaysCreateEntityBusServerFlag = 1 << (FirstFreeFlag + 3)
    }
}
