// MAXConnector Sample Application
// Demonstrates Sales Order creation and an inventory transaction via MAXUpdate DLLs.
// See MAXUpdate Rev 5.6.10 §4 for full function reference.
//
// BEFORE RUNNING:
//   1. Copy MAXORDR2.DLL, MAXTRAN2.DLL, MAXEXEC2.DLL and their dependencies from
//      \\Maxdev\c\EXACT\RMCLIENT\EFW into src\MAXConnector\lib\
//   2. Verify ConnectionString, CompanyName, LicensePath, and EfwPath below.

using MAXConnector;
using MAXConnector.Services;
using MAXConnector.Xml;

// [STAThread] is required — XMLWrapperClass is an Apartment-threaded COM object.
// Without STA, COM marshals calls through a proxy and Initialize() will fault.
internal static class Program
{
    [System.STAThread]
    private static void Main(string[] args)
    {
        // Ensure log directory exists before Initialize() tries to write to it
        var logPath = @"C:\Logs\MAXConnector";
        System.IO.Directory.CreateDirectory(logPath);

        var config = new MaxConnectorConfig
        {
            ConnectionString  = "Provider=sqloledb;Data Source=10.0.2.131;database=ExactMAXPowderRiver;UID=sa;Pwd=TEST123!@#;",
            CompanyName       = "Powder River",
            LicensePath       = @"\\Maxdev\c\EXACT\RMServer\LIC",
            LogPath           = logPath,
            EnableErrorReport = false,
            EfwPath           = @"\\Maxdev\c\EXACT\RMCLIENT\EFW",
        };

        // ── Sales Order ──────────────────────────────────────────────────────
        Console.WriteLine("=== Sales Order Add ===");
        try
        {
            var orderClient = new MaxOrderClient(config);
            orderClient.Initialize();
            Console.WriteLine("  Initialize() OK");

            var soHeader = new Dictionary<string, string>
            {
                ["CUSTID_27"]  = "600",
                ["GLXREF_27"]  = "000410000",
                ["STYPE_27"]   = "CU",
                ["STATUS_27"]  = "3",
                ["ORDDTE_27"]  = XmlEnvelope.FormatDate(DateTime.Today),
                ["REP1_27"]    = "MW01",
                ["SPLIT1_27"]  = "100",
                ["TERMS_27"]   = "02",
                ["SHPVIA_27"]  = "01",
                ["FOB_27"]     = "Shipping Point",
                ["TAXCD1_27"]  = XmlEnvelope.Blank,
                ["TAXCD2_27"]  = XmlEnvelope.Blank,
                ["TAXCD3_27"]  = XmlEnvelope.Blank,
                ["NAME_27"]    = "SAMPLE CUSTOMER",
                ["ADDR1_27"]   = "123 Main St",
                ["CITY_27"]    = "Columbus",
                ["STATE_27"]   = "OH",
                ["ZIPCD_27"]   = "43017",
                ["CNTRY_27"]   = "USA",
                ["PHONE_27"]   = "614-555-0100",
                ["CNTCT_27"]   = "Jane Smith",
                ["FEDTAX_27"]  = "N",
                ["TAXABL_27"]  = "N",
                ["EXCRTE_27"]  = "1",
                ["FIXVAR_27"]  = "F",
                ["CURR_27"]    = "US",
                ["TTAX_27"]    = "0",
                ["LNETAX_27"]  = "N",
            };

            orderClient.AddSalesOrder(soHeader);
            Console.WriteLine("  Sales order header created successfully.");
        }
        catch (MaxConnectorException ex)
        {
            Console.Error.WriteLine($"  Order failed: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.MaxErrorMessage))
                Console.Error.WriteLine($"  MAX error: {ex.MaxErrorMessage}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Unexpected error: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
        }

        // ── Inventory Adjustment ─────────────────────────────────────────────
        Console.WriteLine("\n=== Inventory Adjustment ===");
        try
        {
            var tranClient = new MaxTransactionClient(config);
            tranClient.Initialize();
            Console.WriteLine("  Initialize() OK");

            var adjustment = new Dictionary<string, string>
            {
                ["TRNTYP_39"]   = "ADJ",
                ["TNXDTE_39"]   = XmlEnvelope.FormatTransactionDate(DateTime.Today),
                ["PRTNUM_39"]   = "PART-001",
                ["WARCOD_39"]   = "WH1",
                ["LOCC_39"]     = "BIN-A1",
                ["QTYTRN_39"]   = "10",
                ["UNITMEAS_39"] = "EA",
                ["DOCNUM_39"]   = "ADJ-001",
            };

            tranClient.ProcessTransaction(adjustment);
            Console.WriteLine("  Adjustment transaction processed successfully.");
        }
        catch (MaxConnectorException ex)
        {
            Console.Error.WriteLine($"  Transaction failed: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.MaxErrorMessage))
                Console.Error.WriteLine($"  MAX error: {ex.MaxErrorMessage}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Unexpected error: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
        }

        // ── Job Progress / Time Card ─────────────────────────────────────────
        Console.WriteLine("\n=== Job Progress - Labor Login ===");
        try
        {
            var laborClient = new MaxTransactionClient(config);
            laborClient.Initialize();
            Console.WriteLine("  Initialize() OK");

            // Login employee 1200 to shop order 50000012, operation 0040
            var loginTime = DateTime.Now;
            laborClient.LaborLoginOrder(
                orderNum:     "50000012",
                operationSeq: "0040",
                employeeId:   "1200",
                loginTime:    loginTime,
                shift:        "1");
            Console.WriteLine("  Labor login recorded.");

            // Simulate work, then logout with completed quantity and run time
            Console.WriteLine("\n=== Job Progress - Labor Logout ===");
            var logoutTime = loginTime.AddHours(2);
            laborClient.LaborLogoutOrder(
                orderNum:        "50000012",
                operationSeq:    "0040",
                employeeId:      "1200",
                logoutTime:      logoutTime,
                quantity:        5,
                actualRunTime:   2.0,
                loginTime:       loginTime,
                shift:           "1",
                actualSetupTime: 0.25,
                scrapQty:        0);
            Console.WriteLine("  Labor logout recorded (2h run, 0.25h setup, 5 pcs).");
        }
        catch (MaxConnectorException ex)
        {
            Console.Error.WriteLine($"  Labor failed: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.MaxErrorMessage))
                Console.Error.WriteLine($"  MAX error: {ex.MaxErrorMessage}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Unexpected error: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
        }

        // ── Receipt of Completed Shop Order ──────────────────────────────────
        Console.WriteLine("\n=== Receipt - Shop Order ===");
        try
        {
            var receiptClient = new MaxTransactionClient(config);
            receiptClient.Initialize();
            Console.WriteLine("  Initialize() OK");

            // Receive 10 units from shop order 50000012 into FGI stockroom
            receiptClient.ReceiveShopOrder(
                orderNum:        "50000012",
                quantity:        10,
                transactionTime: DateTime.Now,
                receiveToStock:  "FGI",
                referenceDesc:   "MAXConnector receipt test");
            Console.WriteLine("  Shop order receipt processed (10 pcs to FGI).");
        }
        catch (MaxConnectorException ex)
        {
            Console.Error.WriteLine($"  Receipt failed: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.MaxErrorMessage))
                Console.Error.WriteLine($"  MAX error: {ex.MaxErrorMessage}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Unexpected error: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
        }

        Console.WriteLine("\nDone.");
    }
}
