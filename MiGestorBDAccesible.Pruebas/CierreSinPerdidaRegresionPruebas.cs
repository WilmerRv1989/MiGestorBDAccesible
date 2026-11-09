using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Edicion;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.MySql;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;

namespace MiGestorBDAccesible.Pruebas
{
    [Collection("Cierre sin pérdida")]
    public class CierreSinPerdidaRegresionPruebas
    {
        [Fact]
        public async Task AC2_ElCierreNoEsCorrectoSiAparecenCambiosDuranteLaFinalizacion()
        {
            var primero = new FinalizadorFalso();
            var segundo = new FinalizadorFalso { AlFinalizar = () => primero.Pendientes = true };

            var resultado = await CierreAplicacion.TerminarAsync([primero, segundo], true);

            Assert.True(resultado.QuedanCambios);
            Assert.False(resultado.Correcto);
            Assert.NotEmpty(resultado.Errores);
        }

        [Fact]
        public async Task AC1_NoConfirmaUnaEdicionCuyaTransaccionFueAbortadaPorUnaLectura()
        {
            var conexion = new ConexionAbortable();
            await using var sesion = new SesionEdicionAdo(conexion, ex => ex.Message);
            Assert.True((await sesion.CambiarAsync("INSERT INTO prueba VALUES (1)")).Correcto);
            await Assert.ThrowsAsync<InvalidOperationException>(() => sesion.LeerAsync("SELECT 1 / 0"));

            var error = await sesion.ConfirmarAsync();

            Assert.NotNull(error);
            Assert.Equal(0, conexion.Transaccion.Confirmaciones);
            Assert.Equal(1, sesion.CambiosPendientes);
            Assert.Null(await sesion.DeshacerAsync());
        }

        [Fact]
        public async Task AC3_DeshaceLasFilasAntesDeEsperarAlProveedorBloqueadoPorEllas()
        {
            var liberada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var sesion = new EdicionFalsa(liberada);
            RegistroSesionesEdicion.Agregar(sesion);
            var proveedor = new FinalizadorFalso { Esperar = liberada.Task };
            var cierre = CierreAplicacion.SalirSinGuardarAsync([proveedor]);

            try
            {
                var terminada = await Task.WhenAny(cierre, Task.Delay(TimeSpan.FromSeconds(1)));
                Assert.Same(cierre, terminada);
                await cierre;
                Assert.False(RegistroSesionesEdicion.HayCambiosPendientes);
            }
            finally
            {
                liberada.TrySetResult();
                await cierre;
            }
        }

        [Theory]
        [InlineData(TipoMotor.PostgreSql)]
        [InlineData(TipoMotor.Oracle)]
        [InlineData(TipoMotor.MySql)]
        public async Task AC3_DescartarRespetaElTurnoDeLaSesion(TipoMotor motor)
        {
            var (proveedor, sesion) = RegistrarSesion(motor);
            var turno = (SemaphoreSlim)sesion.GetType().GetProperty("Turno")!.GetValue(sesion)!;
            await turno.WaitAsync();
            var descarte = proveedor.DescartarSesionesAsync(null);

            try
            {
                Assert.False(descarte.IsCompleted);
            }
            finally
            {
                turno.Release();
                await descarte;
            }
        }

        [Fact]
        public async Task AC2_NoPermiteEditarMientrasLaFinalizacionEstaPausada()
        {
            var entrada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var sesion = new SesionEdicionAdo(new ConexionAbortable(), ex => ex.Message);
            var proveedor = new FinalizadorFalso { Entrada = entrada, Esperar = continuar.Task };
            var cierre = CierreAplicacion.TerminarAsync([proveedor], true);
            await entrada.Task;

            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => sesion.CambiarAsync("INSERT INTO prueba VALUES (1)"));
            }
            finally
            {
                continuar.TrySetResult();
                await cierre;
            }
        }

        [Theory]
        [InlineData(TipoMotor.MySql)]
        [InlineData(TipoMotor.MariaDb)]
        public async Task AC3_UnaSesionConDosAliasSoloSeLiberaUnaVez(TipoMotor motor)
        {
            var (proveedor, sesion) = RegistrarSesion(motor);
            var conexion = (DbConnection)sesion.GetType().GetProperty("Conexion")!.GetValue(sesion)!;
            var cierres = 0;
            conexion.Disposed += (_, _) => cierres++;
            var registro = (IDictionary)typeof(ProveedorMySqlBase).GetField("_sesiones", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(proveedor)!;
            var alias = new ConexionActiva(new ConexionModelo { Motor = motor }, null, new ResultadoPruebaConexion(true, TimeSpan.Zero));
            registro.Add(alias, sesion);

            Assert.True((await proveedor.FinalizarSesionAsync(null, false)).Correcto);

            Assert.Equal(1, cierres);
            Assert.Empty(registro);
        }

        [Fact]
        public async Task AC3_ElDescarteTieneUnaEsperaLimitadaSinCerrarDuranteElUso()
        {
            var (proveedor, sesion) = RegistrarSesion(TipoMotor.MySql);
            var conexion = (DbConnection)sesion.GetType().GetProperty("Conexion")!.GetValue(sesion)!;
            var liberada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            conexion.Disposed += (_, _) => liberada.TrySetResult();
            var turno = (SemaphoreSlim)sesion.GetType().GetProperty("Turno")!.GetValue(sesion)!;
            await turno.WaitAsync();
            var descarte = proveedor.DescartarSesionesAsync(null);

            try
            {
                Assert.False(descarte.IsCompleted);
                await descarte.WaitAsync(TimeSpan.FromSeconds(8));
                Assert.False(liberada.Task.IsCompleted);
            }
            finally
            {
                turno.Release();
                await descarte;
                await liberada.Task.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public async Task AC3_SalirSinGuardarCancelaLaEdicionEnCurso()
        {
            var conexion = new ConexionAbortable { EsperarCancelacion = true };
            await using var sesion = new SesionEdicionAdo(conexion, ex => ex.Message);
            using var cancelacion = new CancellationTokenSource();
            var edicion = sesion.CambiarAsync("UPDATE prueba SET id=2", cancelacion.Token);
            await conexion.Entrada.Task;
            var cierre = CierreAplicacion.SalirSinGuardarAsync([]);

            try
            {
                Assert.Same(cierre, await Task.WhenAny(cierre, Task.Delay(TimeSpan.FromSeconds(1))));
                await cierre;
                Assert.False((await edicion).Correcto);
                Assert.False(RegistroSesionesEdicion.HayCambiosPendientes);
            }
            finally
            {
                cancelacion.Cancel();
                try { await edicion; }
                catch (OperationCanceledException) { }
                await cierre;
            }
        }

        private static (IFinalizadorSesiones Proveedor, object Sesion) RegistrarSesion(TipoMotor motor)
        {
            IFinalizadorSesiones proveedor = motor switch
            {
                TipoMotor.PostgreSql => new ProveedorPostgreSql(),
                TipoMotor.Oracle => new ProveedorOracle(),
                TipoMotor.MariaDb => new ProveedorMariaDb(),
                _ => new ProveedorMySql()
            };
            var tipo = motor is TipoMotor.MySql or TipoMotor.MariaDb ? typeof(ProveedorMySqlBase) : proveedor.GetType();
            var tipoSesion = tipo.GetNestedTypes(BindingFlags.NonPublic).Single(t => t.Name.StartsWith("Sesion"));
            var constructor = tipoSesion.GetConstructors().Single();
            var parametros = constructor.GetParameters();
            var conexion = Activator.CreateInstance(parametros[0].ParameterType)!;
            var sesion = constructor.Invoke(parametros.Length == 2 ? [conexion, "postgres"] : [conexion]);
            var registro = (IDictionary)tipo.GetField("_sesiones", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(proveedor)!;
            var activa = new ConexionActiva(new ConexionModelo { Motor = motor }, null, new ResultadoPruebaConexion(true, TimeSpan.Zero));
            registro.Add(motor == TipoMotor.PostgreSql ? (activa, "postgres") : activa, sesion);
            return (proveedor, sesion);
        }

        private sealed class FinalizadorFalso : IFinalizadorSesiones
        {
            public TipoMotor Motor => TipoMotor.PostgreSql;
            public bool Pendientes { get; set; }
            public Action? AlFinalizar { get; init; }
            public Task Esperar { get; init; } = Task.CompletedTask;
            public TaskCompletionSource? Entrada { get; init; }
            public bool TieneCambiosPendientes(ConexionActiva? conexion = null) => Pendientes;

            public async Task<ResultadoFinalizacionModelo> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar)
            {
                Entrada?.TrySetResult();
                await Esperar;
                Pendientes = false;
                AlFinalizar?.Invoke();
                return new ResultadoFinalizacionModelo();
            }
        }

        private sealed class EdicionFalsa(TaskCompletionSource liberada) : ISesionEdicion
        {
            public int CambiosPendientes { get; private set; } = 1;
            public Task<FilasLeidasModelo> LeerAsync(string consulta, CancellationToken cancelacion = default) => throw new NotSupportedException();
            public Task<ResultadoCambioFilaModelo> CambiarAsync(string sentencia, CancellationToken cancelacion = default) => throw new NotSupportedException();
            public Task<string?> ConfirmarAsync() => throw new NotSupportedException();
            public Task<string?> DeshacerAsync()
            {
                CambiosPendientes = 0;
                liberada.TrySetResult();
                return Task.FromResult<string?>(null);
            }
            public ValueTask DisposeAsync()
            {
                RegistroSesionesEdicion.Quitar(this);
                return ValueTask.CompletedTask;
            }
        }

        private sealed class ErrorBaseDatos(string texto) : DbException(texto);

        private sealed class ConexionAbortable : DbConnection
        {
            [AllowNull] public override string ConnectionString { get; set; } = "";
            public override string Database => "Prueba";
            public override string DataSource => "Memoria";
            public override string ServerVersion => "1";
            public override ConnectionState State => ConnectionState.Open;
            public TransaccionAbortable Transaccion { get; private set; } = null!;
            public bool EsperarCancelacion { get; init; }
            public TaskCompletionSource Entrada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public override void Open() { }
            public override void Close() { }
            public override void ChangeDatabase(string nombre) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel aislamiento) => Transaccion = new TransaccionAbortable(this);
            protected override DbCommand CreateDbCommand() => new ComandoAbortable(this);
        }

        private sealed class TransaccionAbortable(ConexionAbortable conexion) : DbTransaction
        {
            public bool Abortada { get; set; }
            public int Confirmaciones { get; private set; }
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            protected override DbConnection DbConnection => conexion;
            public override void Save(string nombre)
            {
                if (Abortada) throw new ErrorBaseDatos("25P02: la transacción está abortada; deshazla con ROLLBACK.");
            }
            public override void Commit() => Confirmaciones++;
            public override void Rollback() => Abortada = false;
            public override void Rollback(string nombre) => Abortada = false;
        }

        private sealed class ComandoAbortable(ConexionAbortable conexion) : DbCommand
        {
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = conexion;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
            public override void Cancel() { }
            public override int ExecuteNonQuery() => 1;
            public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancelacion)
            {
                if (conexion.EsperarCancelacion)
                {
                    conexion.Entrada.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancelacion);
                }
                return 1;
            }
            public override object? ExecuteScalar() => throw new NotSupportedException();
            public override void Prepare() => throw new NotSupportedException();
            protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior comportamiento)
            {
                conexion.Transaccion.Abortada = true;
                throw new ErrorBaseDatos("División entre cero");
            }
        }
    }
}
