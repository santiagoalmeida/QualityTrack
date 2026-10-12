using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualityTrack.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames the model without touching the data: what was a "project" becomes a solution, and what was
    /// a "component" becomes a project. Written by hand because the scaffolded version dropped and
    /// recreated the tables. The order matters: "Projects" must be freed before "Components" takes the name.
    /// </summary>
    public partial class RenameToSolutionsAndProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX "IX_Components_ProjectId_Key";
                DROP INDEX "IX_Goals_ComponentId_MetricKey";
                DROP INDEX "IX_Goals_ProjectId";
                DROP INDEX "IX_Measurements_ComponentId_MetricKey";
                DROP INDEX "IX_Measurements_RunId_ComponentId_MetricKey";
                DROP INDEX "IX_Projects_OrganizationId_Key";
                DROP INDEX "IX_Runs_ProjectId_Branch_ExecutedAt";

                ALTER TABLE "Projects" RENAME TO "Solutions";
                ALTER TABLE "Components" RENAME TO "Projects";

                ALTER TABLE "Projects" RENAME COLUMN "ProjectId" TO "SolutionId";
                ALTER TABLE "Runs" RENAME COLUMN "ProjectId" TO "SolutionId";
                ALTER TABLE "Goals" RENAME COLUMN "ProjectId" TO "SolutionId";
                ALTER TABLE "Goals" RENAME COLUMN "ComponentId" TO "ProjectId";
                ALTER TABLE "Measurements" RENAME COLUMN "ComponentId" TO "ProjectId";

                CREATE UNIQUE INDEX "IX_Solutions_OrganizationId_Key" ON "Solutions" ("OrganizationId", "Key");
                CREATE UNIQUE INDEX "IX_Projects_SolutionId_Key" ON "Projects" ("SolutionId", "Key");
                CREATE INDEX "IX_Runs_SolutionId_Branch_ExecutedAt" ON "Runs" ("SolutionId", "Branch", "ExecutedAt");
                CREATE INDEX "IX_Goals_SolutionId" ON "Goals" ("SolutionId");
                CREATE UNIQUE INDEX "IX_Goals_ProjectId_MetricKey" ON "Goals" ("ProjectId", "MetricKey");
                CREATE INDEX "IX_Measurements_ProjectId_MetricKey" ON "Measurements" ("ProjectId", "MetricKey");
                CREATE UNIQUE INDEX "IX_Measurements_RunId_ProjectId_MetricKey" ON "Measurements" ("RunId", "ProjectId", "MetricKey");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX "IX_Solutions_OrganizationId_Key";
                DROP INDEX "IX_Projects_SolutionId_Key";
                DROP INDEX "IX_Runs_SolutionId_Branch_ExecutedAt";
                DROP INDEX "IX_Goals_SolutionId";
                DROP INDEX "IX_Goals_ProjectId_MetricKey";
                DROP INDEX "IX_Measurements_ProjectId_MetricKey";
                DROP INDEX "IX_Measurements_RunId_ProjectId_MetricKey";

                ALTER TABLE "Measurements" RENAME COLUMN "ProjectId" TO "ComponentId";
                ALTER TABLE "Goals" RENAME COLUMN "ProjectId" TO "ComponentId";
                ALTER TABLE "Goals" RENAME COLUMN "SolutionId" TO "ProjectId";
                ALTER TABLE "Runs" RENAME COLUMN "SolutionId" TO "ProjectId";
                ALTER TABLE "Projects" RENAME COLUMN "SolutionId" TO "ProjectId";

                ALTER TABLE "Projects" RENAME TO "Components";
                ALTER TABLE "Solutions" RENAME TO "Projects";

                CREATE UNIQUE INDEX "IX_Components_ProjectId_Key" ON "Components" ("ProjectId", "Key");
                CREATE UNIQUE INDEX "IX_Goals_ComponentId_MetricKey" ON "Goals" ("ComponentId", "MetricKey");
                CREATE INDEX "IX_Goals_ProjectId" ON "Goals" ("ProjectId");
                CREATE INDEX "IX_Measurements_ComponentId_MetricKey" ON "Measurements" ("ComponentId", "MetricKey");
                CREATE UNIQUE INDEX "IX_Measurements_RunId_ComponentId_MetricKey" ON "Measurements" ("RunId", "ComponentId", "MetricKey");
                CREATE UNIQUE INDEX "IX_Projects_OrganizationId_Key" ON "Projects" ("OrganizationId", "Key");
                CREATE INDEX "IX_Runs_ProjectId_Branch_ExecutedAt" ON "Runs" ("ProjectId", "Branch", "ExecutedAt");
                """);
        }
    }
}
