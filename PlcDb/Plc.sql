-- Plc-NAME create script.
-- Table Environment is from the User.sql
-- Table Session is from the User.sql.

-- PLC configuration
CREATE TABLE Configuration (
	Id integer NOT NULL PRIMARY KEY AUTOINCREMENT,
	CreateDate datetime NOT NULL
);

CREATE TABLE ConfigurationItem (
	Id integer NOT NULL PRIMARY KEY AUTOINCREMENT,
	ConfigurationId integer not null,
	Name nvarchar(200) not null,
	Value varbinary (2147483647) not null,
UNIQUE (ConfigurationId, Name) ON CONFLICT FAIL,
CONSTRAINT FK_ConfigurationItem_ConfigurationId FOREIGN KEY(ConfigurationId) REFERENCES Configuration(Id)
);

-- Signal values.
CREATE TABLE SignalValue(
	Id integer NOT NULL PRIMARY KEY ASC AUTOINCREMENT,
	CreateDate datetime NOT NULL,
	SessionId integer NOT NULL,
	OriginalId integer NOT NULL,
	[TimeStamp] datetime NOT NULL,
	ConfigurationId integer NOT NULL,
	SignalValues varbinary(2048) NOT NULL,
 UNIQUE(ConfigurationId, OriginalId) ON CONFLICT FAIL,
 CONSTRAINT FK_SignalValue_SessionId FOREIGN KEY(SessionId) REFERENCES Session(Id),
 CONSTRAINT FK_SignalValue_ConfigurationId FOREIGN KEY(ConfigurationId) REFERENCES Configuration(Id)
);

CREATE INDEX IDX_SignalValue_TimeStamp  ON SignalValue([TimeStamp]);
