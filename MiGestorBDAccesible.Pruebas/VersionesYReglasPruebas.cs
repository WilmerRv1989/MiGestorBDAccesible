using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class VersionesYReglasPruebas
    {
        [Fact]
        public void SqlServerYOracleNoMuestranLaReglaRestrict()
        {
            var sqlServer = new ProveedorSqlServer().Capacidades;
            var oracle = new ProveedorOracle().Capacidades;

            Assert.Equal([ReglaReferencial.SinAccion, ReglaReferencial.Cascada, ReglaReferencial.EstablecerNulo, ReglaReferencial.EstablecerPredeterminado], sqlServer.ReglasAlEliminar);
            Assert.Equal(sqlServer.ReglasAlEliminar, sqlServer.ReglasAlActualizar);
            Assert.DoesNotContain(ReglaReferencial.Restringir, oracle.ReglasAlEliminar);
            Assert.Empty(oracle.ReglasAlActualizar);
        }

        [Fact]
        public void LaReglaRestrictTieneSuSqlYSuExplicacion()
        {
            Assert.Equal("RESTRICT", ReglaReferencial.Restringir.Sql());
            Assert.StartsWith("Restringir (RESTRICT): no se permite borrar", ReglaReferencial.Restringir.Explicacion(true));
            Assert.StartsWith("Restringir (RESTRICT): no se permite cambiar la clave", ReglaReferencial.Restringir.Explicacion(false));
        }

        [Theory]
        [InlineData(13, true)]
        [InlineData(14, false)]
        [InlineData(16, false)]
        public void SqlServerAvisaDesdeAntesDe2017(int versionPrincipal, bool avisa)
        {
            var aviso = new ProveedorSqlServer().Capacidades.AvisoVersion(versionPrincipal);

            Assert.Equal(avisa, aviso is not null);

            if (avisa)
            {
                Assert.Contains("desde SQL Server 2017", aviso);
            }
        }

        [Theory]
        [InlineData(18, true)]
        [InlineData(19, false)]
        [InlineData(23, false)]
        public void OracleAvisaDesdeAntesDe19c(int versionPrincipal, bool avisa)
        {
            var aviso = new ProveedorOracle().Capacidades.AvisoVersion(versionPrincipal);

            Assert.Equal(avisa, aviso is not null);

            if (avisa)
            {
                Assert.Contains("desde Oracle 19c", aviso);
            }
        }

        [Fact]
        public void PostgreSqlTieneSusDatosDeConexion()
        {
            Assert.Equal("PostgreSQL", TipoMotor.PostgreSql.Nombre());
            Assert.Equal(5432, TipoMotor.PostgreSql.PuertoPredeterminado());
            Assert.True(TipoMotor.PostgreSql.AdmiteBaseInicial());
            Assert.Equal("postgres", TipoMotor.PostgreSql.BaseInicialPredeterminada());
            Assert.True(TipoMotor.SqlServer.AdmiteBaseInicial());
            Assert.Null(TipoMotor.SqlServer.BaseInicialPredeterminada());
            Assert.False(TipoMotor.Oracle.AdmiteBaseInicial());
            Assert.False(TipoMotor.PostgreSql.AdmiteAutenticacionWindows());
        }
    }
}
