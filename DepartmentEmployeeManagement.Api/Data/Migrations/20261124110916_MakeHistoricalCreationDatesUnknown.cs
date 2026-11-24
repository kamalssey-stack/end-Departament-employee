using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DepartmentEmployeeManagement.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeHistoricalCreationDatesUnknown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Employees",
                type: "datetime2",
                nullable: true,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Departments",
                type: "datetime2",
                nullable: true,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.Sql("UPDATE [Employees] SET [CreatedAt] = NULL WHERE [CreatedAt] <= DATEADD(MINUTE, 1, (SELECT MIN([CreatedAt]) FROM [Departments]));");
            migrationBuilder.Sql("UPDATE [Departments] SET [CreatedAt] = NULL WHERE [CreatedAt] = (SELECT MIN([CreatedAt]) FROM [Departments]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Departments] SET [CreatedAt] = GETUTCDATE() WHERE [CreatedAt] IS NULL;");
            migrationBuilder.Sql("UPDATE [Employees] SET [CreatedAt] = GETUTCDATE() WHERE [CreatedAt] IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Employees",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true,
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Departments",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true,
                oldDefaultValueSql: "GETUTCDATE()");
        }
    }
}
