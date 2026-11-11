using MiGestorBDAccesible.Core.Autocompletado;
using MiGestorBDAccesible.Core.Ayuda;
using MiGestorBDAccesible.Core.Conexiones;

namespace MiGestorBDAccesible.Pruebas
{
    public class AyudaPruebas
    {
        private readonly AyudaService _ayuda = new();

        [Fact]
        public void LasFichasDeSqlServerSeCarganSinRepetirIdentificadores()
        {
            var fichas = _ayuda.Contenido(TipoMotor.SqlServer).Fichas;

            Assert.True(fichas.Count >= 40);
            Assert.Equal(fichas.Count, fichas.Select(f => f.Id).Distinct().Count());
        }

        [Fact]
        public void TodosLosTemasRelacionadosExisten()
        {
            var fichas = _ayuda.Contenido(TipoMotor.SqlServer).Fichas;
            var rotos = fichas
                .SelectMany(f => f.Relacionadas.Select(r => (f.Id, Relacionada: r)))
                .Where(par => _ayuda.Obtener(TipoMotor.SqlServer, par.Relacionada) is null)
                .Select(par => $"{par.Id} -> {par.Relacionada}")
                .ToList();

            Assert.Empty(rotos);
        }

        [Fact]
        public void CadaFichaTieneResumenYAlMenosUnaSeccion()
        {
            var incompletas = _ayuda.Contenido(TipoMotor.SqlServer).Fichas
                .Where(f => string.IsNullOrWhiteSpace(f.Resumen) || f.Secciones.Count == 0)
                .Select(f => f.Id)
                .ToList();

            Assert.Empty(incompletas);
        }

        [Theory]
        [InlineData("tipo:nvarchar(150)", "tipo:nvarchar")]
        [InlineData("tipo:decimal(10, 2)", "tipo:decimal")]
        [InlineData("tipo:bigint", "tipo:enteros")]
        [InlineData("palabra:group by", "palabra:group-by")]
        [InlineData("palabra:left join", "palabra:join")]
        [InlineData("palabra:not null", "concepto:null")]
        [InlineData("palabra:cascade", "concepto:reglas-referenciales")]
        [InlineData("concepto:llave-foranea", "concepto:llave-foranea")]
        public void ResuelveTemasDeContexto(string clave, string esperado)
        {
            Assert.Equal(esperado, _ayuda.Resolver(TipoMotor.SqlServer, clave)?.Id);
        }

        [Theory]
        [InlineData(TipoMotor.MySql, "https://dev.mysql.com/doc/refman/8.4/en/")]
        [InlineData(TipoMotor.MariaDb, "https://mariadb.com/docs/server/")]
        public void LasFichasDeMySqlYMariaDbEstanCompletasYSinEnlacesRotos(TipoMotor motor, string fuente)
        {
            var fichas = _ayuda.Contenido(motor).Fichas;
            var rotos = fichas
                .SelectMany(f => f.Relacionadas.Select(r => (f.Id, Relacionada: r)))
                .Where(par => _ayuda.Obtener(motor, par.Relacionada) is null)
                .Select(par => $"{par.Id} -> {par.Relacionada}");
            var incompletas = fichas
                .Where(f => string.IsNullOrWhiteSpace(f.Resumen) || f.Secciones.Count == 0 || (f.Categoria != AyudaService.CategoriaAtajos && f.Fuentes.Count == 0))
                .Select(f => f.Id);

            Assert.True(fichas.Count >= 60);
            Assert.Equal(fichas.Count, fichas.Select(f => f.Id).Distinct().Count());
            Assert.Empty(rotos);
            Assert.Empty(incompletas);
            Assert.All(fichas.SelectMany(f => f.Fuentes), f => Assert.StartsWith(fuente, f.Url));
            Assert.DoesNotContain(fichas, f => f.Resumen.Contains("{M}") || f.Titulo.Contains("{M}"));
        }

        [Theory]
        [InlineData("concepto:tabla")]
        [InlineData("concepto:identity")]
        [InlineData("concepto:vistas")]
        [InlineData("concepto:procedimientos")]
        [InlineData("concepto:funciones")]
        [InlineData("concepto:disparadores")]
        [InlineData("concepto:secuencias")]
        [InlineData("atajos:aplicacion")]
        public void MySqlYMariaDbTienenLosTemasQuePideLaAplicacion(string tema)
        {
            Assert.NotNull(_ayuda.Obtener(TipoMotor.MySql, tema));
            Assert.NotNull(_ayuda.Obtener(TipoMotor.MariaDb, tema));
        }

        [Theory]
        [InlineData("tipo:int(11)", "tipo:enteros")]
        [InlineData("tipo:int unsigned", "tipo:enteros")]
        [InlineData("tipo:decimal(10, 2)", "tipo:decimal")]
        [InlineData("tipo:enum('a','b')", "tipo:enum")]
        [InlineData("tipo:longtext", "tipo:text")]
        [InlineData("palabra:delimiter", "rutinas:delimiter")]
        [InlineData("palabra:auto_increment", "concepto:identity")]
        [InlineData("palabra:limit", "palabra:limit")]
        public void ResuelveLosTiposYLasPalabrasDeMySql(string clave, string esperada)
        {
            Assert.Equal(esperada, _ayuda.Resolver(TipoMotor.MySql, clave)?.Id);
        }

        [Fact]
        public void CadaVarianteHablaDeSuMotor()
        {
            Assert.Contains("MySQL no tiene secuencias", _ayuda.Obtener(TipoMotor.MySql, "concepto:secuencias")!.Resumen);
            Assert.Contains("entrega números", _ayuda.Obtener(TipoMotor.MariaDb, "concepto:secuencias")!.Resumen);
            Assert.Contains("MariaDB", _ayuda.Obtener(TipoMotor.MariaDb, "concepto:bases-esquemas")!.Resumen);
        }

        [Fact]
        public void LasFichasDeLosOtrosMotoresMencionanAMySql()
        {
            foreach (var motor in new[] { TipoMotor.SqlServer, TipoMotor.Oracle, TipoMotor.PostgreSql })
            {
                Assert.Contains(_ayuda.Obtener(motor, "concepto:identity")!.OtrosMotores, o => o.Motor.StartsWith("MySQL"));
            }
        }

        [Fact]
        public void LasFichasDeOracleEstanCompletasYSinEnlacesRotos()
        {
            var fichas = _ayuda.Contenido(TipoMotor.Oracle).Fichas;
            var rotos = fichas
                .SelectMany(f => f.Relacionadas.Select(r => (f.Id, Relacionada: r)))
                .Where(par => _ayuda.Obtener(TipoMotor.Oracle, par.Relacionada) is null)
                .Select(par => $"{par.Id} -> {par.Relacionada}");
            var incompletas = fichas
                .Where(f => string.IsNullOrWhiteSpace(f.Resumen) || f.Secciones.Count == 0 || (f.Categoria != AyudaService.CategoriaAtajos && f.Fuentes.Count == 0))
                .Select(f => f.Id);

            Assert.True(fichas.Count >= 60);
            Assert.Equal(fichas.Count, fichas.Select(f => f.Id).Distinct().Count());
            Assert.Empty(rotos);
            Assert.Empty(incompletas);
            Assert.All(fichas.SelectMany(f => f.Fuentes), fuente => Assert.StartsWith("https://docs.oracle.com/", fuente.Url));
        }

        [Theory]
        [InlineData("concepto:tabla")]
        [InlineData("concepto:null")]
        [InlineData("concepto:clave-principal")]
        [InlineData("concepto:llave-foranea")]
        [InlineData("concepto:reglas-referenciales")]
        [InlineData("concepto:uno-a-muchos")]
        [InlineData("concepto:muchos-a-muchos")]
        [InlineData("concepto:identity")]
        [InlineData("concepto:unique")]
        [InlineData("concepto:default")]
        [InlineData("concepto:check")]
        [InlineData("concepto:indice")]
        [InlineData("concepto:columna-calculada")]
        [InlineData("atajos:aplicacion")]
        public void OracleTieneLosTemasQuePideLaAplicacion(string tema)
        {
            Assert.NotNull(_ayuda.Obtener(TipoMotor.Oracle, tema));
        }

        [Theory]
        [InlineData("tipo:VARCHAR2(100)", "tipo:varchar2")]
        [InlineData("tipo:VARCHAR2(100 CHAR)", "tipo:varchar2")]
        [InlineData("tipo:NUMBER(38, 0)", "tipo:number")]
        [InlineData("tipo:INTEGER", "tipo:integer")]
        [InlineData("tipo:TIMESTAMP(6) WITH TIME ZONE", "tipo:timestamp")]
        [InlineData("tipo:TIMESTAMP WITH TIME ZONE", "tipo:timestamp")]
        [InlineData("tipo:BINARY_DOUBLE", "tipo:binary-double")]
        [InlineData("palabra:fetch", "palabra:fetch-first")]
        [InlineData("palabra:commit", "concepto:transacciones")]
        [InlineData("palabra:loop", "plsql:bucles")]
        [InlineData("palabra:dbms_output", "plsql:dbms-output")]
        [InlineData("palabra:nvl", "palabra:funciones-nulos")]
        [InlineData("palabra:dual", "concepto:dual")]
        [InlineData("palabra:begin", "plsql:bloque")]
        [InlineData("palabra:case", "palabra:decode-case")]
        public void ResuelveTemasDeOracle(string clave, string esperado)
        {
            Assert.Equal(esperado, _ayuda.Resolver(TipoMotor.Oracle, clave)?.Id);
        }

        [Fact]
        public void LasFichasDeSqlServerComparanConOracle()
        {
            var sinOracle = _ayuda.Contenido(TipoMotor.SqlServer).Fichas
                .Where(f => f.Categoria != AyudaService.CategoriaAtajos && f.OtrosMotores.All(n => n.Motor != "Oracle"))
                .Select(f => f.Id);

            Assert.Empty(sinOracle);
        }

        [Fact]
        public void LasFichasDePostgreSqlEstanCompletasYSinEnlacesRotos()
        {
            var contenido = _ayuda.Contenido(TipoMotor.PostgreSql);
            var fichas = contenido.Fichas;
            var rotos = fichas
                .SelectMany(f => f.Relacionadas.Select(r => (f.Id, Relacionada: r)))
                .Where(par => _ayuda.Obtener(TipoMotor.PostgreSql, par.Relacionada) is null)
                .Select(par => $"{par.Id} -> {par.Relacionada}");
            var incompletas = fichas
                .Where(f => string.IsNullOrWhiteSpace(f.Resumen) || f.Secciones.Count == 0 || (f.Categoria != AyudaService.CategoriaAtajos && f.Fuentes.Count == 0))
                .Select(f => f.Id);

            Assert.Equal("PL/pgSQL", contenido.TituloProgramacion);
            Assert.Equal("PL/SQL", _ayuda.Contenido(TipoMotor.Oracle).TituloProgramacion);
            Assert.True(fichas.Count >= 60);
            Assert.Equal(fichas.Count, fichas.Select(f => f.Id).Distinct().Count());
            Assert.Empty(rotos);
            Assert.Empty(incompletas);
            Assert.All(fichas.SelectMany(f => f.Fuentes), fuente => Assert.StartsWith("https://www.postgresql.org/docs/18/", fuente.Url));
        }

        [Theory]
        [InlineData("concepto:tabla")]
        [InlineData("concepto:null")]
        [InlineData("concepto:clave-principal")]
        [InlineData("concepto:llave-foranea")]
        [InlineData("concepto:reglas-referenciales")]
        [InlineData("concepto:uno-a-muchos")]
        [InlineData("concepto:muchos-a-muchos")]
        [InlineData("concepto:identity")]
        [InlineData("concepto:unique")]
        [InlineData("concepto:default")]
        [InlineData("concepto:check")]
        [InlineData("concepto:indice")]
        [InlineData("concepto:columna-calculada")]
        [InlineData("atajos:aplicacion")]
        public void PostgreSqlTieneLosTemasQuePideLaAplicacion(string tema)
        {
            Assert.NotNull(_ayuda.Obtener(TipoMotor.PostgreSql, tema));
        }

        [Theory]
        [InlineData("tipo:varchar(100)", "tipo:varchar")]
        [InlineData("tipo:integer", "tipo:enteros")]
        [InlineData("tipo:bigint", "tipo:enteros")]
        [InlineData("tipo:numeric(10, 2)", "tipo:numeric")]
        [InlineData("tipo:timestamptz(3)", "tipo:timestamp")]
        [InlineData("tipo:double precision", "tipo:flotantes")]
        [InlineData("tipo:integer[]", "tipo:arreglos")]
        [InlineData("tipo:jsonb", "tipo:jsonb")]
        [InlineData("palabra:limit", "palabra:limit")]
        [InlineData("palabra:ilike", "palabra:where")]
        [InlineData("palabra:returning", "palabra:insert")]
        [InlineData("palabra:begin", "concepto:transacciones")]
        [InlineData("palabra:loop", "plpgsql:bucles")]
        [InlineData("palabra:raise", "plpgsql:raise")]
        [InlineData("palabra:do", "plpgsql:do")]
        [InlineData("palabra:coalesce", "palabra:funciones-nulos-case")]
        public void ResuelveTemasDePostgreSql(string clave, string esperado)
        {
            Assert.Equal(esperado, _ayuda.Resolver(TipoMotor.PostgreSql, clave)?.Id);
        }

        [Fact]
        public void LasFichasDeOracleComparanConPostgreSql()
        {
            var sinPostgreSql = _ayuda.Contenido(TipoMotor.Oracle).Fichas
                .Where(f => f.Categoria != AyudaService.CategoriaAtajos && f.OtrosMotores.All(n => !n.Motor.Contains("PostgreSQL")))
                .Select(f => f.Id);

            Assert.Empty(sinPostgreSql);
        }

        [Theory]
        [InlineData(TipoMotor.SqlServer)]
        [InlineData(TipoMotor.Oracle)]
        [InlineData(TipoMotor.PostgreSql)]
        [InlineData(TipoMotor.MySql)]
        [InlineData(TipoMotor.MariaDb)]
        public void TodaPalabraSugeridaTieneFicha(TipoMotor motor)
        {
            var vocabulario = AutocompletadoService.Vocabulario(motor);
            var sinFicha = vocabulario.Palabras.Concat(vocabulario.Funciones)
                .Where(p => _ayuda.Resolver(motor, $"{AyudaService.CategoriaPalabra}:{p}") is null)
                .Concat(vocabulario.Tipos.Where(t => _ayuda.Resolver(motor, $"{AyudaService.CategoriaTipo}:{t}") is null));

            Assert.Empty(sinFicha);
        }

        [Theory]
        [InlineData("palabra:getdate", "palabra:fechas")]
        [InlineData("palabra:begin try", "tsql:errores")]
        [InlineData("palabra:else", "palabra:case")]
        [InlineData("palabra:@@rowcount", "tsql:funciones-sistema")]
        [InlineData("palabra:newid", "tipo:uniqueidentifier")]
        [InlineData("palabra:string_agg", "palabra:group-by")]
        [InlineData("palabra:delete from", "palabra:delete")]
        public void ResuelveLasPalabrasDeTSql(string clave, string esperado)
        {
            Assert.Equal(esperado, _ayuda.Resolver(TipoMotor.SqlServer, clave)?.Id);
        }

        [Theory]
        [InlineData("palabra:minus", "palabra:union")]
        [InlineData("palabra:connect by", "palabra:connect-by")]
        [InlineData("palabra:regexp_like", "palabra:funciones-texto")]
        [InlineData("palabra:to_number", "palabra:conversion")]
        [InlineData("palabra:execute immediate", "plsql:execute-immediate")]
        [InlineData("palabra:returning", "plsql:variables")]
        [InlineData("palabra:is", "plsql:procedimientos")]
        [InlineData("palabra:last_day", "palabra:fechas")]
        public void ResuelveLasPalabrasNuevasDeOracle(string clave, string esperado)
        {
            Assert.Equal(esperado, _ayuda.Resolver(TipoMotor.Oracle, clave)?.Id);
        }

        [Theory]
        [InlineData("palabra:with recursive", "palabra:with")]
        [InlineData("palabra:except", "palabra:union")]
        [InlineData("palabra:row_number", "palabra:ventana")]
        [InlineData("palabra:random", "palabra:funciones-numericas")]
        [InlineData("palabra:merge into", "palabra:on-conflict")]
        [InlineData("palabra:return query", "plpgsql:funciones")]
        [InlineData("palabra:json_agg", "tipo:jsonb")]
        [InlineData("palabra:create schema", "concepto:bases-esquemas")]
        public void ResuelveLasPalabrasNuevasDePostgreSql(string clave, string esperado)
        {
            Assert.Equal(esperado, _ayuda.Resolver(TipoMotor.PostgreSql, clave)?.Id);
        }

        [Theory]
        [InlineData(TipoMotor.MySql, "palabra:with recursive", "palabra:with")]
        [InlineData(TipoMotor.MySql, "palabra:intersect", "palabra:union")]
        [InlineData(TipoMotor.MySql, "palabra:row_number", "palabra:ventana")]
        [InlineData(TipoMotor.MySql, "palabra:greatest", "palabra:funciones-numericas")]
        [InlineData(TipoMotor.MySql, "palabra:with rollup", "palabra:group-by")]
        [InlineData(TipoMotor.MySql, "palabra:exists", "palabra:where")]
        [InlineData(TipoMotor.MySql, "palabra:str_to_date", "palabra:fechas")]
        [InlineData(TipoMotor.MySql, "palabra:json_object", "tipo:json")]
        [InlineData(TipoMotor.MySql, "palabra:for each row", "concepto:disparadores")]
        [InlineData(TipoMotor.MariaDb, "palabra:create sequence", "concepto:secuencias")]
        [InlineData(TipoMotor.MariaDb, "palabra:create or replace", "concepto:vistas")]
        [InlineData(TipoMotor.MariaDb, "palabra:uuid", "concepto:identity")]
        public void ResuelveLasPalabrasNuevasDeMySqlYMariaDb(TipoMotor motor, string clave, string esperado)
        {
            Assert.Equal(esperado, _ayuda.Resolver(motor, clave)?.Id);
        }

        [Fact]
        public void CreateOrReplaceSeExplicaDistintoEnMySqlYMariaDb()
        {
            var mysql = string.Join(' ', _ayuda.Obtener(TipoMotor.MySql, "concepto:vistas")!.Secciones.SelectMany(s => s.Parrafos));
            var mariadb = string.Join(' ', _ayuda.Obtener(TipoMotor.MariaDb, "concepto:vistas")!.Secciones.SelectMany(s => s.Parrafos));

            Assert.Contains("solo existe para las vistas", mysql);
            Assert.Contains("sirve también con tablas", mariadb);
        }

        [Fact]
        public void LasFichasDeSqlServerAgrupanTSqlYCitanMicrosoftLearn()
        {
            var contenido = _ayuda.Contenido(TipoMotor.SqlServer);

            Assert.Equal("T-SQL", contenido.TituloProgramacion);
            Assert.Contains(contenido.Fichas, f => f.Categoria == AyudaService.CategoriaPlSql);
            Assert.All(contenido.Fichas.Where(f => f.Categoria == AyudaService.CategoriaPlSql), f => Assert.StartsWith("tsql:", f.Id));
            Assert.All(contenido.Fichas.SelectMany(f => f.Fuentes), fuente => Assert.StartsWith("https://learn.microsoft.com/es-es/", fuente.Url));
        }

        [Fact]
        public void BuscaSinImportarAcentosNiMayusculas()
        {
            var resultados = _ayuda.Buscar(TipoMotor.SqlServer, "INDICE");

            Assert.Contains(resultados, f => f.Id == "concepto:indice");
        }
    }
}
