-- Plc-NAME create script.

-- Environment.
CREATE TABLE Environment (
	Id integer NOT NULL PRIMARY KEY AUTOINCREMENT,
	Name nvarchar(200) not null UNIQUE,
	Value nvarchar(1024) not null
);

-- PLC Session
CREATE TABLE Session (
	Id integer NOT NULL PRIMARY KEY AUTOINCREMENT,
	StartTime datetime NOT NULL,
	EndTime datetime NOT NULL,
	IpAddress nvarchar(16) NOT NULL,
	IsOpen bit NOT NULL
);
