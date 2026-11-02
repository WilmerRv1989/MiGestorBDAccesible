using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;

namespace MiGestorBDAccesible.Pruebas
{
    public class PostgreSqlPruebas
    {
        private readonly ProveedorPostgreSql _proveedor = new();

        [Fact]
        public void DeclaraSusCapacidades()
        {
            var capacidades = _proveedor.Capacidades;

            Assert.Equal("Bases de datos", capacidades.ContenedoresTitulo);
            Assert.Equal("Bases de datos del sistema", capacidades.ContenedoresSistemaTitulo);
            Assert.Contains(ReglaReferencial.Restringir, capacidades.ReglasAlEliminar);
            Assert.Contains(ReglaReferencial.Restringir, capacidades.ReglasAlActualizar);
            Assert.Contains(ReglaReferencial.EstablecerPredeterminado, capacidades.ReglasAlActualizar);
            Assert.False(capacidades.Terminos.TieneIndicesAgrupados);
            Assert.Equal("public", _proveedor.EsquemaPredeterminado(null!));
            Assert.Equal("postgres", _proveedor.BaseDatosAdministracion);
            Assert.Null(capacidades.AvisoVersion(18));
            Assert.Contains("desde PostgreSQL 14", capacidades.AvisoVersion(13));
        }

        [Theory]
        [InlineData("character varying(100)", "varchar(100)")]
        [InlineData("character varying", "varchar")]
        [InlineData("character(2)", "char(2)")]
        [InlineData("numeric(10,2)", "numeric(10, 2)")]
        [InlineData("timestamp without time zone", "timestamp")]
        [InlineData("timestamp with time zone", "timestamptz")]
        [InlineData("timestamp(3) with time zone", "timestamptz(3)")]
        [InlineData("time without time zone", "time")]
        [InlineData("integer[]", "integer[]")]
        [InlineData("double precision", "double precision")]
        public void AcortaLosNombresDeTipo(string original, string esperado)
        {
            Assert.Equal(esperado, ProveedorPostgreSql.FormatearTipo(original));
        }

        [Theory]
        [InlineData(0, 1, 7200000000L, "1 día 02:00:00")]
        [InlineData(14, 0, 0L, "1 año 2 meses")]
        [InlineData(0, 0, 0L, "00:00:00")]
        [InlineData(0, 0, 1500000L, "00:00:01.5")]
        [InlineData(0, 3, -3600000000L, "3 días -01:00:00")]
        public void MuestraLosIntervalosEnEspanol(int meses, int dias, long microsegundos, string esperado)
        {
            Assert.Equal(esperado, ProveedorPostgreSql.FormatearIntervalo(new NpgsqlTypes.NpgsqlInterval(meses, dias, microsegundos)));
        }

        [Theory]
        [InlineData("clientes", "clientes")]
        [InlineData("pedido_productos", "pedido_productos")]
        [InlineData("Clientes", "\"Clientes\"")]
        [InlineData("user", "\"user\"")]
        [InlineData("Nombre Raro", "\"Nombre Raro\"")]
        public void LosNombresSoloLlevanComillasCuandoHacenFalta(string nombre, string esperado)
        {
            Assert.Equal(esperado, ProveedorPostgreSql.Identificador(nombre));
        }

        [Fact]
        public void LosScriptsDeBasesYTablasUsanElDialectoDePostgreSql()
        {
            Assert.Equal("CREATE DATABASE practica2;", _proveedor.ScriptCrearBaseDatos("practica2"));
            Assert.Equal("DROP DATABASE \"BasePrueba\" WITH (FORCE);", _proveedor.ScriptEliminarBaseDatos("BasePrueba"));
            Assert.Equal("DROP TABLE clientes;", _proveedor.ScriptEliminarTabla(new TablaModelo { Esquema = "public", Nombre = "clientes" }));
            Assert.Equal("DROP TABLE ventas.facturas;", _proveedor.ScriptEliminarTabla(new TablaModelo { Esquema = "ventas", Nombre = "facturas" }));
            Assert.True(_proveedor.Capacidades.PuedeCrearContenedores);
            Assert.True(_proveedor.Capacidades.DdlTransaccional);
            Assert.False(_proveedor.Capacidades.TienePapelera);
        }

        [Fact]
        public void LaLlaveNoValidadaSeExplicaConSuTermino()
        {
            var terminos = _proveedor.Capacidades.Terminos;

            Assert.Equal("NOT VALID", terminos.LlaveDeshabilitada);
            Assert.StartsWith("Las filas que ya existían no se comprobaron", terminos.FraseLlaveDeshabilitada);
        }

        [Fact]
        public void LaCadenaDeConexionUsaLaBaseInicialOPostgres()
        {
            var sinBase = new ConexionModelo { Motor = TipoMotor.PostgreSql, Servidor = "localhost", Puerto = 5433, Usuario = "tienda" };
            var conBase = new ConexionModelo { Motor = TipoMotor.PostgreSql, Servidor = "localhost", Puerto = 5433, Usuario = "tienda", BaseDatosInicial = "tiendapractica" };

            Assert.Contains("Database=postgres", ProveedorPostgreSql.CadenaConexion(sinBase, "x"));
            Assert.Contains("Database=tiendapractica", ProveedorPostgreSql.CadenaConexion(conBase, "x"));
            Assert.Contains("Database=otra", ProveedorPostgreSql.CadenaConexion(conBase, "x", "otra"));
            Assert.Contains("Port=5433", ProveedorPostgreSql.CadenaConexion(sinBase, "x"));
        }
    }
}
