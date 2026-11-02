using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class CapacidadesMotorPruebas
    {
        [Fact]
        public void SqlServerConservaElComportamientoDeLaVersion11()
        {
            var proveedor = new ProveedorSqlServer();
            var capacidades = proveedor.Capacidades;

            Assert.Equal("Bases de datos", capacidades.ContenedoresTitulo);
            Assert.Equal("Bases de datos del sistema", capacidades.ContenedoresSistemaTitulo);
            Assert.True(capacidades.PuedeCrearContenedores);
            Assert.Equal(4, capacidades.ReglasAlEliminar.Count);
            Assert.Equal(4, capacidades.ReglasAlActualizar.Count);
            Assert.True(capacidades.Terminos.TieneIndicesAgrupados);
            Assert.Equal("IDENTITY(1, 1)", capacidades.Terminos.Identidad(1, 1));
            Assert.Equal("dbo", proveedor.EsquemaPredeterminado(null!));
        }

        [Fact]
        public void OracleTieneSusDatosDeConexion()
        {
            Assert.Equal("Oracle", TipoMotor.Oracle.Nombre());
            Assert.Equal(1521, TipoMotor.Oracle.PuertoPredeterminado());
            Assert.True(TipoMotor.Oracle.UsaNombreServicio());
            Assert.False(TipoMotor.Oracle.AdmiteAutenticacionWindows());
            Assert.Equal("FREEPDB1", TipoMotor.Oracle.ServicioPredeterminado());
            Assert.True(TipoMotor.SqlServer.AdmiteAutenticacionWindows());
            Assert.False(TipoMotor.SqlServer.UsaNombreServicio());
        }

        [Fact]
        public void OracleDeclaraSusCapacidades()
        {
            var proveedores = new ProveedoresService();
            var oracle = proveedores.Obtener(TipoMotor.Oracle);

            Assert.True(proveedores.EstaDisponible(TipoMotor.SqlServer));
            Assert.True(proveedores.EstaDisponible(TipoMotor.Oracle));
            Assert.True(proveedores.EstaDisponible(TipoMotor.PostgreSql));
            Assert.False(proveedores.EstaDisponible(TipoMotor.MySql));
            Assert.Equal("Esquemas", oracle.Capacidades.ContenedoresTitulo);
            Assert.False(oracle.Capacidades.PuedeCrearContenedores);
            Assert.Empty(oracle.Capacidades.ReglasAlActualizar);
            Assert.DoesNotContain(ReglaReferencial.EstablecerPredeterminado, oracle.Capacidades.ReglasAlEliminar);
            Assert.False(oracle.Capacidades.Terminos.TieneIndicesAgrupados);
            Assert.Equal("GENERATED AS IDENTITY", oracle.Capacidades.Terminos.Identidad(1, 1));
            Assert.Equal("GENERATED AS IDENTITY (START WITH 100 INCREMENT BY 10)", oracle.Capacidades.Terminos.Identidad(100, 10));
        }

        [Fact]
        public void LaCadenaDeConexionDeOracleUsaElNombreDeServicio()
        {
            var conexion = new ConexionModelo { Motor = TipoMotor.Oracle, Servidor = "localhost", Puerto = 1521, NombreServicio = "FREEPDB1", Usuario = "tienda" };
            var cadena = Core.Proveedores.Oracle.ProveedorOracle.CadenaConexion(conexion, "x");

            Assert.Contains("//localhost:1521/FREEPDB1", cadena);
            Assert.Contains("tienda", cadena, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LeeConexionesGuardadasConLaVersion11()
        {
            var carpeta = Path.Combine(Path.GetTempPath(), $"mgbda-{Guid.NewGuid():N}");
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, "conexiones.json");

            await File.WriteAllTextAsync(ruta, """
                [
                  {
                    "Id": "7f2c1d5e-0000-4000-8000-000000000001",
                    "Nombre": "practica",
                    "Motor": "SqlServer",
                    "Servidor": "localhost",
                    "Puerto": 1433,
                    "Autenticacion": "UsuarioYContrasena",
                    "Usuario": "sa",
                    "ContrasenaCifrada": null,
                    "ConfiarCertificado": true,
                    "BaseDatosInicial": null
                  }
                ]
                """);

            try
            {
                var conexion = Assert.Single(await new AlmacenConexionesService(ruta).ListaAsync());

                Assert.Equal(TipoMotor.SqlServer, conexion.Motor);
                Assert.Null(conexion.NombreServicio);
                Assert.Equal("SQL Server, localhost, puerto 1433, usuario sa", conexion.Resumen);
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        [Fact]
        public void ElNarradorUsaLosTerminosDelMotor()
        {
            var terminos = new TerminosMotorModelo
            {
                Identidad = (_, _) => "GENERATED AS IDENTITY",
                TieneIndicesAgrupados = false
            };

            var detalle = new DetalleTablaModelo
            {
                Tabla = new TablaModelo { Nombre = "Clientes" },
                Columnas = [new ColumnaModelo { Nombre = "Id", TipoDatos = "NUMBER", EsIdentidad = true, SemillaIdentidad = 1, IncrementoIdentidad = 1 }],
                Indices = [new IndiceModelo { Nombre = "PK_CLIENTES", EsClavePrincipal = true, EsUnico = true, Columnas = [new ColumnaIndiceModelo { Nombre = "Id" }] }]
            };

            var narrador = new NarradorTablaService();
            var columna = narrador.DescribirColumna(detalle.Columnas[0], detalle, terminos);
            var indice = NarradorTablaService.DescribirIndice(detalle.Indices[0], terminos);

            Assert.Contains("(GENERATED AS IDENTITY)", columna);
            Assert.DoesNotContain("IDENTITY(1, 1)", columna);
            Assert.Equal("PK_CLIENTES: índice de la clave principal (PRIMARY KEY), sobre Id.", indice);
        }
    }
}
