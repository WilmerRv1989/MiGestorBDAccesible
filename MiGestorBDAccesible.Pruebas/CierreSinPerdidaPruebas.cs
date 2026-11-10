using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Edicion;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Pruebas
{
    [CollectionDefinition("Cierre sin pérdida", DisableParallelization = true)]
    public class ColeccionCierreSinPerdida
    {
    }

    [Collection("Cierre sin pérdida")]
    public class CierreSinPerdidaPruebas : IAsyncLifetime
    {
        private readonly List<ISesionEdicion> sesiones = [];

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            foreach (var sesion in sesiones)
            {
                RegistroSesionesEdicion.Quitar(sesion);
                await sesion.DisposeAsync();
            }
        }

        [Fact]
        public async Task AC1_ElRegistroConservaLaSesionCuyoCommitFallaYDevuelveElError()
        {
            const string error = "No se pudo confirmar la edición";
            var fallida = Registrar(new SesionFalsa { ErrorConfirmacion = error });
            var correcta = Registrar(new SesionFalsa());

            var errores = await RegistroSesionesEdicion.TerminarTodasAsync(true);

            Assert.Contains(error, Assert.Single(errores));
            Assert.Equal(1, fallida.Confirmaciones);
            Assert.Equal(1, correcta.Confirmaciones);
            Assert.Equal(0, fallida.Cierres);
            Assert.Equal(1, correcta.Cierres);
            Assert.Equal(1, fallida.CambiosPendientes);
            Assert.True(RegistroSesionesEdicion.HayCambiosPendientes);

            RegistroSesionesEdicion.Quitar(fallida);
            Assert.False(RegistroSesionesEdicion.HayCambiosPendientes);
        }

        [Fact]
        public async Task AC1_SesionAdoConservaLosCambiosSiLaTransaccionSigueAbierta()
        {
            var conexion = new ConexionFalsa(ConsecuenciaFallo.ConservarTransaccion);
            var sesion = CrearSesionAdo(conexion);
            await CambiarUnaFilaAsync(sesion);
            var transaccion = Assert.Single(conexion.Transacciones);

            var error = await sesion.ConfirmarAsync();

            Assert.NotNull(error);
            Assert.Contains("Fallo al confirmar", error);
            Assert.Equal(1, transaccion.Confirmaciones);
            Assert.Same(conexion, transaccion.Connection);
            Assert.Equal(ConnectionState.Open, conexion.State);
            Assert.Equal(1, sesion.CambiosPendientes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AC1_SesionAdoInformaYLimpiaSiElServidorDeshizoLaTransaccion(bool conservaConexion)
        {
            var conexion = new ConexionFalsa(conservaConexion
                ? ConsecuenciaFallo.DeshacerTransaccionConConexionAbierta
                : ConsecuenciaFallo.DeshacerTransaccion);
            var sesion = CrearSesionAdo(conexion);
            await CambiarUnaFilaAsync(sesion);
            var transaccion = Assert.Single(conexion.Transacciones);

            var error = await sesion.ConfirmarAsync();

            Assert.NotNull(error);
            Assert.Contains("Fallo al confirmar", error);
            if (!conservaConexion)
            {
                Assert.Null(transaccion.Connection);
            }

            Assert.Equal(ConnectionState.Open, conexion.State);
            Assert.Equal(0, sesion.CambiosPendientes);
            Assert.True(transaccion.Desechada);

            await CambiarUnaFilaAsync(sesion);

            Assert.Equal(2, conexion.Transacciones.Count);
            Assert.NotSame(transaccion, conexion.Transacciones[1]);
            Assert.Same(conexion, conexion.Transacciones[1].Connection);
            Assert.Equal(1, sesion.CambiosPendientes);
        }

        [Fact]
        public async Task AC1_SesionAdoInformaSiSePerdioLaConexion()
        {
            var cambiosTrasElFallo = new List<int>();

            foreach (var consecuencia in new[] { ConsecuenciaFallo.RomperConexion, ConsecuenciaFallo.CerrarConexion })
            {
                var conexion = new ConexionFalsa(consecuencia);
                var sesion = CrearSesionAdo(conexion);
                await CambiarUnaFilaAsync(sesion);

                var error = await sesion.ConfirmarAsync();

                Assert.NotNull(error);
                Assert.Contains("Fallo al confirmar", error);
                Assert.Equal(consecuencia == ConsecuenciaFallo.RomperConexion ? ConnectionState.Broken : ConnectionState.Closed, conexion.State);
                cambiosTrasElFallo.Add(sesion.CambiosPendientes);
            }

            Assert.All(cambiosTrasElFallo, cambios => Assert.Equal(0, cambios));
        }

        [Fact]
        public async Task AC2_ElCierreReuneElMotorYElErrorYOfreceReintentarSiQuedanCambios()
        {
            const string error = "ORA-02091: transacción deshecha";
            var proveedor = new FinalizadorFalso(TipoMotor.Oracle) { ErrorConfirmacion = error };

            var resultado = await CierreAplicacion.TerminarAsync([proveedor], true);

            Assert.False(resultado.Correcto);
            Assert.True(resultado.QuedanCambios);
            Assert.Contains(resultado.Errores, texto => texto.Contains("Oracle") && texto.Contains(error));
            var llamada = Assert.Single(proveedor.Finalizaciones);
            Assert.Null(llamada.Conexion);
            Assert.True(llamada.Confirmar);
            Assert.True(CierreAplicacion.OfreceReintentar(resultado, true));
            Assert.False(CierreAplicacion.OfreceReintentar(resultado, false));

            var aviso = CierreAplicacion.TextoFallo(resultado, true);

            Assert.Contains("Oracle", aviso);
            Assert.Contains(error, aviso);
            Assert.Contains("COMMIT", aviso);
            Assert.Contains("ROLLBACK", aviso);
            Assert.Contains("Cancelar", aviso);
        }

        [Fact]
        public async Task AC2_NoOfreceReintentarSiElServidorYaDeshizoLosCambios()
        {
            const string error = "ORA-02091: transacción deshecha";
            var proveedor = new FinalizadorFalso(TipoMotor.Oracle)
            {
                ErrorConfirmacion = error,
                ConservaCambiosTrasFallo = false
            };

            var resultado = await CierreAplicacion.TerminarAsync([proveedor], true);

            Assert.False(resultado.Correcto);
            Assert.Contains(resultado.Errores, texto => texto.Contains("Oracle") && texto.Contains(error));
            Assert.False(resultado.QuedanCambios);
            Assert.False(CierreAplicacion.OfreceReintentar(resultado, true));
            Assert.False(CierreAplicacion.OfreceReintentar(resultado, false));

            var aviso = CierreAplicacion.TextoFallo(resultado, true);

            Assert.Contains("Oracle", aviso);
            Assert.Contains(error, aviso);
            Assert.Contains("Aceptar", aviso);
            Assert.Contains("Cancelar", aviso);
        }

        [Fact]
        public async Task AC2_ElCierreSinFallosEsCorrecto()
        {
            var proveedores = new[] { new FinalizadorFalso(TipoMotor.SqlServer), new FinalizadorFalso(TipoMotor.PostgreSql) };
            var sesion = Registrar(new SesionFalsa());

            var resultado = await CierreAplicacion.TerminarAsync(proveedores, true);

            Assert.True(resultado.Correcto);
            Assert.Empty(resultado.Errores);
            Assert.False(resultado.QuedanCambios);
            Assert.False(CierreAplicacion.HayCambiosPendientes(proveedores));
            Assert.False(RegistroSesionesEdicion.HayCambiosPendientes);
            Assert.Equal(1, sesion.Confirmaciones);
            Assert.Equal(1, sesion.Cierres);
            Assert.All(proveedores, proveedor =>
            {
                var llamada = Assert.Single(proveedor.Finalizaciones);
                Assert.Null(llamada.Conexion);
                Assert.True(llamada.Confirmar);
                Assert.False(proveedor.TieneCambiosPendientes());
            });
        }

        [Fact]
        public async Task AC3_SalirSinGuardarHaceRollbackYDescartaAunqueFalle()
        {
            var proveedor = new FinalizadorFalso(TipoMotor.Oracle) { ErrorDeshacer = "Fallo al deshacer la consulta" };
            var sesion = Registrar(new SesionFalsa { ErrorDeshacer = "Fallo al deshacer la edición" });

            await CierreAplicacion.SalirSinGuardarAsync([proveedor]);

            var llamada = Assert.Single(proveedor.Finalizaciones);
            Assert.Null(llamada.Conexion);
            Assert.False(llamada.Confirmar);
            Assert.Null(Assert.Single(proveedor.Descartes));
            Assert.False(proveedor.TieneCambiosPendientes());
            Assert.Equal(1, sesion.Deshaceres);
            Assert.Equal(0, sesion.Confirmaciones);
            Assert.Equal(1, sesion.Cierres);
            Assert.Equal(0, sesion.CambiosPendientes);
            Assert.False(RegistroSesionesEdicion.HayCambiosPendientes);
            Assert.False(CierreAplicacion.HayCambiosPendientes([proveedor]));
        }

        private SesionFalsa Registrar(SesionFalsa sesion)
        {
            sesiones.Add(sesion);
            RegistroSesionesEdicion.Agregar(sesion);
            return sesion;
        }

        private SesionEdicionAdo CrearSesionAdo(ConexionFalsa conexion)
        {
            var sesion = new SesionEdicionAdo(conexion, excepcion => excepcion.Message);
            sesiones.Add(sesion);
            return sesion;
        }

        private static async Task CambiarUnaFilaAsync(ISesionEdicion sesion)
        {
            var cambio = await sesion.CambiarAsync("UPDATE clientes SET nombre = 'Ana' WHERE id = 1");
            Assert.True(cambio.Correcto);
            Assert.Equal(1, cambio.FilasAfectadas);
            Assert.Equal(1, sesion.CambiosPendientes);
        }

        private sealed class SesionFalsa : ISesionEdicion
        {
            public int CambiosPendientes { get; private set; } = 1;

            public string? ErrorConfirmacion { get; init; }

            public string? ErrorDeshacer { get; init; }

            public int Confirmaciones { get; private set; }

            public int Deshaceres { get; private set; }

            public int Cierres { get; private set; }

            public Task<FilasLeidasModelo> LeerAsync(string consulta, CancellationToken cancelacion = default) => throw new NotSupportedException();

            public Task<ResultadoCambioFilaModelo> CambiarAsync(string sentencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

            public Task<string?> ConfirmarAsync()
            {
                Confirmaciones++;

                if (ErrorConfirmacion is null)
                {
                    CambiosPendientes = 0;
                }

                return Task.FromResult(ErrorConfirmacion);
            }

            public Task<string?> DeshacerAsync()
            {
                Deshaceres++;

                if (ErrorDeshacer is null)
                {
                    CambiosPendientes = 0;
                }

                return Task.FromResult(ErrorDeshacer);
            }

            public ValueTask DisposeAsync()
            {
                Cierres++;
                CambiosPendientes = 0;
                RegistroSesionesEdicion.Quitar(this);
                return ValueTask.CompletedTask;
            }
        }

        private sealed class FinalizadorFalso(TipoMotor motor) : IFinalizadorSesiones
        {
            public TipoMotor Motor { get; } = motor;

            public string? ErrorConfirmacion { get; init; }

            public string? ErrorDeshacer { get; init; }

            public bool ConservaCambiosTrasFallo { get; init; } = true;

            private bool cambiosPendientes = true;

            public List<(ConexionActiva? Conexion, bool Confirmar)> Finalizaciones { get; } = [];

            public List<ConexionActiva?> Descartes { get; } = [];

            public bool TieneCambiosPendientes(ConexionActiva? conexion = null) => cambiosPendientes;

            public Task<ResultadoFinalizacionModelo> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar)
            {
                Finalizaciones.Add((conexion, confirmar));
                var error = confirmar ? ErrorConfirmacion : ErrorDeshacer;

                if (error is null || !ConservaCambiosTrasFallo)
                {
                    cambiosPendientes = false;
                }

                return Task.FromResult(new ResultadoFinalizacionModelo { Errores = error is null ? [] : [error] });
            }

            public Task DescartarSesionesAsync(ConexionActiva? conexion)
            {
                Descartes.Add(conexion);
                cambiosPendientes = false;
                return Task.CompletedTask;
            }
        }

        private enum ConsecuenciaFallo
        {
            ConservarTransaccion,
            DeshacerTransaccion,
            DeshacerTransaccionConConexionAbierta,
            RomperConexion,
            CerrarConexion
        }

        private sealed class ExcepcionBaseDatosFalsa() : DbException("Fallo al confirmar");

        private sealed class ConexionFalsa(ConsecuenciaFallo consecuencia) : DbConnection
        {
            private ConnectionState estado = ConnectionState.Open;

            [AllowNull]
            public override string ConnectionString { get; set; } = "";

            public override string Database => "Pruebas";

            public override string DataSource => "Doble en memoria";

            public override string ServerVersion => "1";

            public override ConnectionState State => estado;

            public List<TransaccionFalsa> Transacciones { get; } = [];

            public override void Open() => estado = ConnectionState.Open;

            public override void Close() => estado = ConnectionState.Closed;

            public override void ChangeDatabase(string nombre) => throw new NotSupportedException();

            protected override DbTransaction BeginDbTransaction(IsolationLevel aislamiento)
            {
                if (estado != ConnectionState.Open)
                {
                    throw new InvalidOperationException("La conexión de prueba debe estar abierta");
                }

                var transaccion = new TransaccionFalsa(this, consecuencia);
                Transacciones.Add(transaccion);
                return transaccion;
            }

            protected override DbCommand CreateDbCommand() => new ComandoFalso(this);

            public void Romper() => estado = ConnectionState.Broken;
        }

        private sealed class TransaccionFalsa(ConexionFalsa conexion, ConsecuenciaFallo consecuencia) : DbTransaction
        {
            private DbConnection? asociada = conexion;

            private bool terminada;

            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

            protected override DbConnection? DbConnection => asociada;

            public int Confirmaciones { get; private set; }

            public bool Desechada { get; private set; }

            public override void Commit()
            {
                VerificarActiva();
                Confirmaciones++;

                switch (consecuencia)
                {
                    case ConsecuenciaFallo.DeshacerTransaccion:
                        terminada = true;
                        asociada = null;
                        break;
                    case ConsecuenciaFallo.DeshacerTransaccionConConexionAbierta:
                        terminada = true;
                        break;
                    case ConsecuenciaFallo.RomperConexion:
                        conexion.Romper();
                        break;
                    case ConsecuenciaFallo.CerrarConexion:
                        conexion.Close();
                        break;
                }

                throw new ExcepcionBaseDatosFalsa();
            }

            public override void Rollback()
            {
                VerificarActiva();
                terminada = true;
                asociada = null;
            }

            public override void Rollback(string nombre) => VerificarActiva();

            public override void Save(string nombre)
            {
                VerificarActiva();

                if (asociada?.State != ConnectionState.Open || Desechada)
                {
                    throw new InvalidOperationException("No puedes reutilizar una transacción terminada");
                }
            }

            public void VerificarActiva()
            {
                if (terminada)
                {
                    throw new InvalidOperationException("This NpgsqlTransaction has completed; it is no longer usable.");
                }
            }

            protected override void Dispose(bool desechar)
            {
                terminada = true;
                Desechada = true;
                asociada = null;
                base.Dispose(desechar);
            }
        }

        private sealed class ComandoFalso(ConexionFalsa conexion) : DbCommand
        {
            [AllowNull]
            public override string CommandText { get; set; } = "";

            public override int CommandTimeout { get; set; }

            public override CommandType CommandType { get; set; }

            public override bool DesignTimeVisible { get; set; }

            public override UpdateRowSource UpdatedRowSource { get; set; }

            protected override DbConnection? DbConnection { get; set; } = conexion;

            protected override DbTransaction? DbTransaction { get; set; }

            protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();

            public override int ExecuteNonQuery()
            {
                VerificarTransaccion();

                if (DbTransaction?.Connection != DbConnection || DbConnection?.State != ConnectionState.Open)
                {
                    throw new InvalidOperationException("El comando de prueba necesita una transacción abierta");
                }

                return 1;
            }

            public override void Cancel() => throw new NotSupportedException();

            public override object? ExecuteScalar()
            {
                VerificarTransaccion();
                throw new NotSupportedException();
            }

            public override void Prepare()
            {
                VerificarTransaccion();
                throw new NotSupportedException();
            }

            protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior comportamiento)
            {
                VerificarTransaccion();
                throw new NotSupportedException();
            }

            private void VerificarTransaccion() => (DbTransaction as TransaccionFalsa)?.VerificarActiva();
        }
    }
}
