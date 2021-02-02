/*
Post-Deployment Script Template              
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.    
 Use SQLCMD syntax to include a file in the post-deployment script.      
 Example:      :r .\myfile.sql                
 Use SQLCMD syntax to reference a variable in the post-deployment script.    
 Example:      :setvar TableName MyTable              
               SELECT * FROM [$(TableName)]          
--------------------------------------------------------------------------------------
*/
If Not Exists(SELECT * FROM UserProfile WHERE Id = '2ba3faed-ce16-46df-8b95-ab0ef26e8ad6')
BEGIN
  INSERT INTO UserProfile(Id, [Name], EmailAddress, IsAdmin)
    VALUES('2ba3faed-ce16-46df-8b95-ab0ef26e8ad6','dev','dev@test.com', 0)
END

If Not Exists(SELECT * FROM UserProfile WHERE Id = '1efb2983-09be-47a5-ac2c-bff124d542ec')
BEGIN
  INSERT INTO UserProfile(Id, [Name], EmailAddress, IsAdmin)
    VALUES('1efb2983-09be-47a5-ac2c-bff124d542ec','admin','admin@test.com', 1)
END