using Xunit;
using Teikem.Infrastructure.Persistence.Scripts;

namespace Teikem.Tests;

// db-update (2026-10-06): el comparador de esquema es lógica pura; se prueba con modelos armados a mano (sin SQL Server).
public class SchemaDifferTests
{
    private static ColumnDef Col(string name, string type = "INT", bool nullable = false, string? def = null, string? defName = null, bool identity = false) =>
        new(name, type, nullable, identity, 1, 1, null, false, null, defName, def, defName is null);

    private static TableDef Table(string name, params ColumnDef[] cols)
    {
        var t = new TableDef("dbo", name);
        t.Columns.AddRange(cols);
        return t;
    }

    private static SchemaModel Model(params TableDef[] tables)
    {
        var m = new SchemaModel();
        foreach (var t in tables) m.Tables[t.Key] = t;
        return m;
    }

    [Fact]
    public void Sin_diferencias_no_hay_cambios()
    {
        var t = () => Table("Product", Col("ProductId", identity: true), Col("Name", "NVARCHAR(100)"));
        var r = SchemaDiffer.Diff(Model(t()), Model(t()));
        Assert.Empty(r.Changes);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Tabla_nueva_se_crea_con_columnas_clave_checks_y_luego_indices_y_fks()
    {
        var d = Table("Rental", Col("RentalId", identity: true), Col("TenantId"), Col("Note", "NVARCHAR(MAX)", nullable: true, def: "(N'')", defName: "DF_Rental_Note"));
        d.Primary = new KeyDef("PK_Rental", false, true, "CLUSTERED", ["RentalId"]);
        d.Checks.Add(new CheckDef("CK_Rental_Tenant", false, "([TenantId]>(0))"));
        d.Indexes.Add(new IndexDef("IX_Rental_Tenant", false, "NONCLUSTERED", [new IndexColumn("TenantId", false)], [], null));
        d.Fks.Add(new FkDef("FK_Rental_Tenant", false, ["TenantId"], "dbo.Tenant", ["TenantId"], "NO_ACTION", "NO_ACTION"));
        var tenant = Table("Tenant", Col("TenantId"));
        var r = SchemaDiffer.Diff(Model(d, tenant), Model(tenant));

        Assert.Equal([1, 5, 8], r.Changes.Select(c => c.Phase).ToArray());
        var create = r.Changes[0].Sql;
        Assert.Contains("CREATE TABLE [dbo].[Rental]", create);
        Assert.Contains("[RentalId] INT IDENTITY(1,1) NOT NULL", create);
        Assert.Contains("[Note] NVARCHAR(MAX) NULL CONSTRAINT [DF_Rental_Note] DEFAULT (N'')", create);
        Assert.Contains("CONSTRAINT [PK_Rental] PRIMARY KEY CLUSTERED ([RentalId])", create);
        Assert.Contains("CONSTRAINT [CK_Rental_Tenant] CHECK ([TenantId]>(0))", create);
        Assert.Equal("CREATE NONCLUSTERED INDEX [IX_Rental_Tenant] ON [dbo].[Rental] ([TenantId])", r.Changes[1].Sql);
        Assert.Equal("ALTER TABLE [dbo].[Rental] ADD CONSTRAINT [FK_Rental_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenant] ([TenantId])", r.Changes[2].Sql);
    }

    [Fact]
    public void Columna_nueva_con_default_se_agrega_en_la_fase_de_columnas_antes_que_los_checks()
    {
        var d = Table("Tenant", Col("TenantId"), Col("RegionCode", "CHAR(2)", def: "('PR')", defName: "DF_Tenant_Region"));
        d.Checks.Add(new CheckDef("CK_Tenant_Region", false, "([RegionCode]='PR')"));
        var a = Table("Tenant", Col("TenantId"));
        var r = SchemaDiffer.Diff(Model(d), Model(a));

        Assert.Equal([2, 6], r.Changes.Select(c => c.Phase).ToArray());
        Assert.Equal("ALTER TABLE [dbo].[Tenant] ADD [RegionCode] CHAR(2) NOT NULL CONSTRAINT [DF_Tenant_Region] DEFAULT ('PR')", r.Changes[0].Sql);
    }

    [Fact]
    public void Check_con_nombre_cambiado_se_retira_y_se_recrea()
    {
        var d = Table("NumberSequence", Col("Id"));
        d.Checks.Add(new CheckDef("CK_Kind", false, "([Kind] IN ('A','B','RENTAL'))"));
        var a = Table("NumberSequence", Col("Id"));
        a.Checks.Add(new CheckDef("CK_Kind", false, "([Kind] IN ('A','B'))"));
        var r = SchemaDiffer.Diff(Model(d), Model(a));

        Assert.Equal([3, 6], r.Changes.Select(c => c.Phase).ToArray());
        Assert.Equal("ALTER TABLE [dbo].[NumberSequence] DROP CONSTRAINT [CK_Kind]", r.Changes[0].Sql);
        Assert.Contains("ADD CONSTRAINT [CK_Kind] CHECK ([Kind] IN ('A','B','RENTAL'))", r.Changes[1].Sql);
    }

    [Fact]
    public void Fk_y_check_sin_nombre_propio_se_comparan_por_firma_no_por_el_nombre_que_puso_sql_server()
    {
        var d = Table("Bin", Col("BinId"), Col("ZoneId"));
        d.Fks.Add(new FkDef("FK__Bin__ZoneId__AAAA", true, ["ZoneId"], "dbo.Zone", ["ZoneId"], "NO_ACTION", "NO_ACTION"));
        d.Checks.Add(new CheckDef("CK__Bin__BBBB", true, "([ZoneId]>(0))"));
        var a = Table("Bin", Col("BinId"), Col("ZoneId"));
        a.Fks.Add(new FkDef("FK__Bin__ZoneId__ZZZZ", true, ["ZoneId"], "dbo.Zone", ["ZoneId"], "NO_ACTION", "NO_ACTION"));
        a.Checks.Add(new CheckDef("CK__Bin__YYYY", true, "([ZoneId]>(0))"));
        Assert.Empty(SchemaDiffer.Diff(Model(d), Model(a)).Changes);
    }

    [Fact]
    public void Columna_varchar_mas_corta_se_ensancha_sola_pero_otro_cambio_de_tipo_solo_avisa()
    {
        var d = Table("P", Col("Name", "NVARCHAR(200)"), Col("Qty", "DECIMAL(16,3)"));
        var a = Table("P", Col("Name", "NVARCHAR(100)"), Col("Qty", "INT"));
        var r = SchemaDiffer.Diff(Model(d), Model(a));

        Assert.Single(r.Changes);
        Assert.Equal("ALTER TABLE [dbo].[P] ALTER COLUMN [Name] NVARCHAR(200) NOT NULL", r.Changes[0].Sql);
        Assert.Single(r.Warnings);
        Assert.Contains("P.Qty", r.Warnings[0]);
    }

    [Fact]
    public void Volver_nula_una_columna_se_hace_sola_pero_volverla_obligatoria_solo_avisa()
    {
        var d = Table("P", Col("A", "INT", nullable: true), Col("B", "INT", nullable: false));
        var a = Table("P", Col("A", "INT", nullable: false), Col("B", "INT", nullable: true));
        var r = SchemaDiffer.Diff(Model(d), Model(a));

        Assert.Equal("ALTER TABLE [dbo].[P] ALTER COLUMN [A] INT NULL", Assert.Single(r.Changes).Sql);
        Assert.Contains("P.B", Assert.Single(r.Warnings));
    }

    [Fact]
    public void Columna_obsoleta_not_null_sin_default_se_retira_solo_si_la_tabla_esta_vacia()
    {
        var d = Table("Charge", Col("ChargeId"));
        var a = Table("Charge", Col("ChargeId"), Col("ContractId"));
        a.Fks.Add(new FkDef("FK_Charge_Contract", false, ["ContractId"], "dbo.Contract", ["ContractId"], "NO_ACTION", "NO_ACTION"));
        a.Indexes.Add(new IndexDef("IX_Charge_Contract", false, "NONCLUSTERED", [new IndexColumn("ContractId", false)], [], null));

        var vacia = Model(a);
        vacia.EmptyTables.Add("dbo.Charge");
        var r1 = SchemaDiffer.Diff(Model(d), vacia);
        Assert.Equal(["ALTER TABLE [dbo].[Charge] DROP CONSTRAINT [FK_Charge_Contract]", "DROP INDEX [IX_Charge_Contract] ON [dbo].[Charge]", "ALTER TABLE [dbo].[Charge] DROP COLUMN [ContractId]"],
            r1.Changes.Select(c => c.Sql).ToArray());

        var r2 = SchemaDiffer.Diff(Model(d), Model(a));
        Assert.Empty(r2.Changes);
        Assert.Contains("Charge.ContractId", Assert.Single(r2.Warnings));
    }

    [Fact]
    public void Lo_que_sobra_en_la_base_nunca_se_borra_solo_se_informa()
    {
        var d = Table("A", Col("Id"));
        var r = SchemaDiffer.Diff(Model(d), Model(Table("A", Col("Id"), Col("Old", nullable: true)), Table("Legacy", Col("X"))));
        Assert.Empty(r.Changes);
        Assert.Contains("Tabla dbo.Legacy", r.Extras);
        Assert.Contains("Columna dbo.A.Old", r.Extras);
    }

    [Fact]
    public void Indice_que_cambio_de_columnas_se_recrea_y_vista_distinta_se_altera()
    {
        var d = Table("A", Col("Id"), Col("X"), Col("Y"));
        d.Indexes.Add(new IndexDef("IX_A", true, "NONCLUSTERED", [new IndexColumn("X", false), new IndexColumn("Y", true)], [], "([Y]>(0))"));
        var a = Table("A", Col("Id"), Col("X"), Col("Y"));
        a.Indexes.Add(new IndexDef("IX_A", false, "NONCLUSTERED", [new IndexColumn("X", false)], [], null));
        var desired = Model(d);
        desired.Views["dbo.V"] = new ViewDef("dbo", "V", "CREATE VIEW dbo.V AS SELECT 2 AS N");
        var actual = Model(a);
        actual.Views["dbo.V"] = new ViewDef("dbo", "V", "CREATE VIEW dbo.V AS SELECT 1 AS N");
        var r = SchemaDiffer.Diff(desired, actual);

        Assert.Equal([3, 5, 9], r.Changes.Select(c => c.Phase).ToArray());
        Assert.Equal("CREATE UNIQUE NONCLUSTERED INDEX [IX_A] ON [dbo].[A] ([X], [Y] DESC) WHERE ([Y]>(0))", r.Changes[1].Sql);
        Assert.StartsWith("ALTER VIEW dbo.V", r.Changes[2].Sql);
    }

    [Theory]
    [InlineData("NVARCHAR(100)", "NVARCHAR(200)", true)]
    [InlineData("VARCHAR(50)", "VARCHAR(MAX)", true)]
    [InlineData("NVARCHAR(200)", "NVARCHAR(100)", false)]
    [InlineData("VARCHAR(50)", "NVARCHAR(100)", false)]
    [InlineData("DECIMAL(10,2)", "DECIMAL(12,2)", false)]
    public void Solo_se_ensancha_automaticamente_el_texto_variable(string from, string to, bool expected) =>
        Assert.Equal(expected, SchemaDiffer.IsWidening(from, to));

    [Theory]
    [InlineData("nvarchar", 200, 0, 0, "NVARCHAR(100)")]
    [InlineData("nvarchar", -1, 0, 0, "NVARCHAR(MAX)")]
    [InlineData("varchar", 40, 0, 0, "VARCHAR(40)")]
    [InlineData("decimal", 17, 18, 4, "DECIMAL(18,4)")]
    [InlineData("datetime2", 8, 27, 7, "DATETIME2(7)")]
    [InlineData("timestamp", 8, 0, 0, "ROWVERSION")]
    [InlineData("uniqueidentifier", 16, 0, 0, "UNIQUEIDENTIFIER")]
    public void El_tipo_se_escribe_como_en_el_ddl(string type, int maxLength, int precision, int scale, string expected) =>
        Assert.Equal(expected, SchemaReader.FormatType(type, maxLength, precision, scale));
}
