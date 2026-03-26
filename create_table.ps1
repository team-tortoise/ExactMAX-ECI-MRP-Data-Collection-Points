$conn = New-Object System.Data.SqlClient.SqlConnection("Server=10.0.2.131;Database=ExactMAXPowderRiver;User Id=sa;Password=TEST123!@#;TrustServerCertificate=True")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ShopFloor_TimeEntry') CREATE TABLE ShopFloor_TimeEntry (EntryId INT IDENTITY(1,1) PRIMARY KEY, OrderNum VARCHAR(20) NOT NULL, OperationSeq VARCHAR(10) NOT NULL, EmployeeId VARCHAR(20) NOT NULL, WorkDate DATE NOT NULL DEFAULT GETDATE(), RunHours FLOAT NOT NULL DEFAULT 0, SetupHours FLOAT NOT NULL DEFAULT 0, QtyCompleted FLOAT NOT NULL DEFAULT 0, QtyScrap FLOAT NOT NULL DEFAULT 0, Notes VARCHAR(500) NULL, CreatedAt DATETIME NOT NULL DEFAULT GETDATE(), PostedToMax BIT NOT NULL DEFAULT 0)"
$cmd.ExecuteNonQuery()
Write-Host "ShopFloor_TimeEntry table ready"
$cmd.CommandText = "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ShopFloor_ActiveSession') CREATE TABLE ShopFloor_ActiveSession (SessionId INT IDENTITY(1,1) PRIMARY KEY, EmployeeId VARCHAR(20) NOT NULL, OrderNum VARCHAR(20) NOT NULL, OperationSeq VARCHAR(10) NOT NULL, Shift VARCHAR(5) NOT NULL DEFAULT '1', StartTime DATETIME NOT NULL DEFAULT GETDATE(), OverrideRunHours FLOAT NULL, OverrideSetupHours FLOAT NULL, CONSTRAINT UQ_ActiveSession_Employee UNIQUE (EmployeeId))"
$cmd.ExecuteNonQuery()
Write-Host "ShopFloor_ActiveSession table ready"
$cmd.CommandText = "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ShopFloor_SessionHistory') CREATE TABLE ShopFloor_SessionHistory (HistoryId INT IDENTITY(1,1) PRIMARY KEY, SessionId INT NOT NULL, EmployeeId VARCHAR(20) NOT NULL, OrderNum VARCHAR(20) NOT NULL, OperationSeq VARCHAR(10) NOT NULL, Shift VARCHAR(5) NOT NULL DEFAULT '1', StartTime DATETIME NOT NULL, EndTime DATETIME NOT NULL DEFAULT GETDATE(), RunHours FLOAT NOT NULL DEFAULT 0, SetupHours FLOAT NOT NULL DEFAULT 0, QtyCompleted FLOAT NOT NULL DEFAULT 0, QtyScrap FLOAT NOT NULL DEFAULT 0, Abandoned BIT NOT NULL DEFAULT 0)"
$cmd.ExecuteNonQuery()
Write-Host "ShopFloor_SessionHistory table ready"
$conn.Close()
