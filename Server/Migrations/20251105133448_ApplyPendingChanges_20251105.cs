using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class ApplyPendingChanges_20251105 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SenderName",
                table: "Transactions",
                newName: "SenderDiscordName");

            migrationBuilder.RenameColumn(
                name: "ReceiverName",
                table: "Transactions",
                newName: "ReceiverDiscordName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SenderDiscordName",
                table: "Transactions",
                newName: "SenderName");

            migrationBuilder.RenameColumn(
                name: "ReceiverDiscordName",
                table: "Transactions",
                newName: "ReceiverName");
        }
    }
}
