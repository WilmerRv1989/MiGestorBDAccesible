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

        [Fact]
        public void BuscaSinImportarAcentosNiMayusculas()
        {
            var resultados = _ayuda.Buscar(TipoMotor.SqlServer, "INDICE");

            Assert.Contains(resultados, f => f.Id == "concepto:indice");
        }
    }
}
