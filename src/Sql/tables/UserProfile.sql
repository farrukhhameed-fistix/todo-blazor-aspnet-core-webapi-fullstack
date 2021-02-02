﻿CREATE TABLE [dbo].[UserProfile]
(
  [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, 
    [Name] VARCHAR(255) NULL, 
    [EmailAddress] VARCHAR(255) NOT NULL, 
    [IsAdmin] BIT NOT NULL
)
