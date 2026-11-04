using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;
using System.Globalization;

namespace MiGestorBDAccesible.Pruebas
{
    public class DivisionAntesDeConexionPruebas
    {
        [Fact]
        public async Task AC3_SqlServerRechazaElScriptAntesDeConectarYDaLaLineaDelEditor()
        {
            var conexion = ConexionInalcanzable(TipoMotor.SqlServer);
            var script = "SELECT 1;\nGO\nSELECT 2;\nGO 99999999999";
            var lineaInicial = 20;

            var resultado = await new ProveedorSqlServer().EjecutarAsync(conexion, null, script, lineaInicial);

            var mensaje = Assert.Single(resultado.Mensajes);
            Assert.Equal(TipoMensaje.Error, mensaje.Tipo);
            Assert.False(string.IsNullOrWhiteSpace(mensaje.Texto));
            Assert.DoesNotContain("127.0.0.1", mensaje.Texto);
            Assert.Equal(lineaInicial + 4 - 1, mensaje.Linea);
            Assert.Contains(DivisorLotes.RepeticionesMaximas.ToString(CultureInfo.InvariantCulture), mensaje.Texto);
            var error = Assert.Throws<ScriptNoDivisibleException>(() => DivisorLotes.Dividir(script));
            Assert.Equal(error.Message, mensaje.Texto);
            Assert.Empty(resultado.Conjuntos);
            Assert.Equal(0, resultado.FilasAfectadas);
        }

        [Theory]
        [InlineData("$tag$")]
        [InlineData("$función$")]
        public async Task AC5_PostgreSqlRechazaElScriptAntesDeConectarYDaLaLineaDelEditor(string etiqueta)
        {
            var conexion = ConexionInalcanzable(TipoMotor.PostgreSql);
            var script = $"SELECT 1;\n\nDO\n{etiqueta}\nBEGIN\n  UPDATE cuentas SET saldo = 0;\nEND;";
            var lineaInicial = 30;

            var resultado = await new ProveedorPostgreSql().EjecutarAsync(conexion, null, script, lineaInicial);

            var mensaje = Assert.Single(resultado.Mensajes);
            Assert.Equal(TipoMensaje.Error, mensaje.Tipo);
            Assert.False(string.IsNullOrWhiteSpace(mensaje.Texto));
            Assert.DoesNotContain("127.0.0.1", mensaje.Texto);
            Assert.Equal(lineaInicial + 4 - 1, mensaje.Linea);
            var error = Assert.Throws<ScriptNoDivisibleException>(() => DivisorPostgreSql.Dividir(script));
            Assert.Equal(error.Message, mensaje.Texto);
            Assert.Empty(resultado.Conjuntos);
            Assert.Equal(0, resultado.FilasAfectadas);
        }

        private static ConexionActiva ConexionInalcanzable(TipoMotor motor) => new(
            new ConexionModelo
            {
                Motor = motor,
                Servidor = "127.0.0.1",
                Puerto = 1,
                Usuario = "prueba",
                BaseDatosInicial = motor == TipoMotor.SqlServer ? "master" : "postgres"
            },
            null,
            new ResultadoPruebaConexion(true, TimeSpan.Zero));
    }
}
