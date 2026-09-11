using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCardCreatorAndColumnEntered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "column_entered_at",
                table: "task_cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "creator_name",
                table: "task_cards",
                type: "text",
                nullable: true);

            // Backfill de tarjetas existentes:
            // 1) creator_name: nombre del actor de la actividad mas antigua de la tarjeta
            //    (tipicamente el "creo la tarjeta").
            migrationBuilder.Sql(@"
                UPDATE task_cards c
                SET creator_name = sub.actor_name
                FROM (
                    SELECT DISTINCT ON (task_card_id) task_card_id, actor_name
                    FROM task_card_activities
                    ORDER BY task_card_id, created_at ASC
                ) sub
                WHERE sub.task_card_id = c.id AND c.creator_name IS NULL;");

            // 2) column_entered_at: base = created_at; luego se sobreescribe con la
            //    fecha del ultimo movimiento de columna registrado en la actividad.
            migrationBuilder.Sql(@"
                UPDATE task_cards SET column_entered_at = created_at
                WHERE column_entered_at IS NULL;");
            migrationBuilder.Sql(@"
                UPDATE task_cards c
                SET column_entered_at = sub.moved_at
                FROM (
                    SELECT DISTINCT ON (task_card_id) task_card_id, created_at AS moved_at
                    FROM task_card_activities
                    WHERE text LIKE 'movio la tarjeta%'
                    ORDER BY task_card_id, created_at DESC
                ) sub
                WHERE sub.task_card_id = c.id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "column_entered_at",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "creator_name",
                table: "task_cards");
        }
    }
}
