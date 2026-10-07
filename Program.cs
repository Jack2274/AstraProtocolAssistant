using Spectre.Console;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// Sem UTF-8 o console do Windows não exibe as setas e o Spectre usa bordas simplificadas.
Console.OutputEncoding = Encoding.UTF8;

const string Titulo = "[bold]Astra Protocol Assistant[/]";

// Telas de referência, nas mesmas teclas de função usadas pelo Astra Protocol 2.
(ConsoleKey Tecla, string Nome)[] telas =
[
    (ConsoleKey.F1, "HELM"),
    (ConsoleKey.F2, "ENGINEERING"),
    (ConsoleKey.F3, "SCIENCE"),
    (ConsoleKey.F4, "ORDNANCE"),
    (ConsoleKey.F5, "NAVIGATION"),
    (ConsoleKey.F6, "OPERATIONS"),
];

// Campos da calculadora de coordenadas (nave X/Y, scan X/Y), mantidos enquanto o programa estiver aberto.
string[] camposCalculadora = ["", "", "", ""];

// O título da janela e o cursor (que não tem uso aqui) são alterados durante o programa e restaurados na saída.
// Só o Windows permite ler o título atual; nos demais sistemas ele é apenas definido.
var tituloAnterior = OperatingSystem.IsWindows() ? Console.Title : "";
Console.Title = "Astra Protocol Assistant";
Console.CursorVisible = false;

try
{
    // Começa na tela principal, no idioma salvo. Só na primeira vez (sem idioma salvo, ou com um que não existe
    // mais) abre antes a escolha de idioma, e Esc nela encerra o programa.
    var salvo = LerIdiomaSalvo();
    var idioma = LerIdiomas().Any(i => i.Codigo.Equals(salvo, StringComparison.OrdinalIgnoreCase))
        ? salvo
        : EscolherIdioma(primeiraVez: true);

    // F8 na tela principal abre a escolha de idioma; Esc lá volta sem mudar nada.
    while (idioma is not null && ExibirTelas(idioma))
        idioma = EscolherIdioma(primeiraVez: false) ?? idioma;
}
catch (Exception erro)
{
    // Sem isto a janela fecharia na hora e a mensagem de erro se perderia.
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine("[red]Unexpected error / Erro inesperado:[/]");
    AnsiConsole.WriteLine(erro.ToString());
    AguardarParaSair();
}
finally
{
    Console.CursorVisible = true;
    Console.Title = tituloAnterior;
}

// Mantém a janela aberta para que uma mensagem de erro possa ser lida antes de o programa fechar.
void AguardarParaSair()
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Press any key to exit / Pressione uma tecla para sair[/]");
    Console.ReadKey(intercept: true);
}

// Os arquivos de texto ficam na pasta Data, ao lado do executável.
string CaminhoDe(string arquivo) => Path.Combine(AppContext.BaseDirectory, "Data", arquivo);

// Aguarda uma tecla, redesenhando a tela sempre que a janela do terminal muda de tamanho.
ConsoleKeyInfo LerTecla(Action desenhar)
{
    var largura = Console.WindowWidth;
    var altura = Console.WindowHeight;

    while (!Console.KeyAvailable)
    {
        if (Console.WindowWidth != largura || Console.WindowHeight != altura)
        {
            largura = Console.WindowWidth;
            altura = Console.WindowHeight;
            desenhar();
        }

        Thread.Sleep(50);
    }

    return Console.ReadKey(intercept: true);
}

// Copia o texto pelo clip.exe do Windows. Não envia quebra de linha no fim, para o comando não ser
// executado sozinho ao colar no jogo.
bool CopiarParaAreaDeTransferencia(string texto)
{
    if (!OperatingSystem.IsWindows())
        return false;

    try
    {
        using var processo = Process.Start(new ProcessStartInfo("clip.exe")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (processo is null)
            return false;

        processo.StandardInput.Write(texto);
        processo.StandardInput.Close();

        return processo.WaitForExit(2000) && processo.ExitCode == 0;
    }
    catch (Exception)
    {
        return false;
    }
}

// Listas com uma linha por item no formato CHAVE|Texto (LANGUAGE.txt, PROCEDURES_*.txt);
// linhas vazias ou iniciadas com # são ignoradas.
List<(string Chave, string Texto)> LerLista(string arquivo)
{
    var caminho = CaminhoDe(arquivo);

    if (!File.Exists(caminho))
        return [];

    return File.ReadAllLines(caminho)
        .Select(linha => linha.Trim())
        .Where(linha => linha.Length > 0 && !linha.StartsWith('#'))
        .Select(linha => linha.Split('|', 2))
        .Select(partes => (partes[0].Trim(), partes.Length > 1 ? partes[1].Trim() : partes[0].Trim()))
        .ToList();
}

List<(string Codigo, string Nome)> LerIdiomas() => LerLista("LANGUAGE.txt");

// A preferência de idioma fica na pasta do usuário (%APPDATA%\AstraProtocolAssistant), e não ao lado
// do executável, que pode estar numa pasta sem permissão de escrita.
string ArquivoPreferencias() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AstraProtocolAssistant", "settings.txt");

// Falhas ao ler ou gravar a preferência são ignoradas: o programa só volta a começar no primeiro idioma.
string? LerIdiomaSalvo()
{
    try
    {
        return File.ReadLines(ArquivoPreferencias())
            .Select(linha => linha.Split('=', 2))
            .Where(partes => partes.Length == 2 && partes[0].Trim() == "Idioma")
            .Select(partes => partes[1].Trim())
            .FirstOrDefault();
    }
    catch (Exception)
    {
        return null;
    }
}

void SalvarIdioma(string codigo)
{
    try
    {
        var arquivo = ArquivoPreferencias();
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        File.WriteAllText(arquivo, $"Idioma={codigo}\n");
    }
    catch (Exception)
    {
    }
}

// Procedimentos operacionais do idioma, com o inglês como alternativa quando a tradução não existe.
List<(string Id, string Titulo)> LerProcedimentos(string idioma)
{
    var procedimentos = LerLista(Path.Combine("Procedures", $"PROCEDURES_{idioma}.txt"));

    return procedimentos.Count > 0 ? procedimentos : LerLista(Path.Combine("Procedures", "PROCEDURES_ENG.txt"));
}

// Devolve o idioma confirmado, ou null se o usuário apertar Esc. Na primeira vez Esc encerra o programa;
// depois, apenas volta para a tela principal, e a legenda indica isso.
string? EscolherIdioma(bool primeiraVez)
{
    var idiomas = LerIdiomas();

    if (idiomas.Count == 0)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[red]LANGUAGE.txt not found or empty / não encontrado ou vazio:[/] {Markup.Escape(CaminhoDe("LANGUAGE.txt"))}");
        AguardarParaSair();
        return null;
    }

    // Começa no último idioma escolhido; sem preferência salva, no primeiro da lista.
    var salvo = LerIdiomaSalvo();
    var selecionado = Math.Max(0, idiomas.FindIndex(i => i.Codigo.Equals(salvo, StringComparison.OrdinalIgnoreCase)));

    void Desenhar()
    {
        // Os textos da tela acompanham o idioma destacado; sem tradução, ficam em inglês e português.
        var textos = LerTextos(idiomas[selecionado].Codigo);
        string Texto(string chave, string padrao) => Markup.Escape(textos.GetValueOrDefault(chave, padrao));

        var tabela = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Expand()
            .Title(Titulo)
            .Caption(primeiraVez
                ? $"[grey]{Texto("LegendaIdioma", "↑ ↓ choose / escolher · → or Enter OK · Esc exit / sair")}[/]"
                : $"[grey]{Texto("LegendaIdiomaVoltar", "↑ ↓ choose / escolher · → or Enter OK · Esc back / voltar")}[/]")
            .AddColumn($"[bold]{Texto("ColunaIdioma", "Language / Idioma")}[/]");

        for (var i = 0; i < idiomas.Count; i++)
        {
            var estilo = i == selecionado ? "black on yellow" : "default";
            tabela.AddRow($"[{estilo}]{Markup.Escape(idiomas[i].Nome)}[/]");
        }

        AnsiConsole.Clear();
        AnsiConsole.Write(tabela);

        // A tabela ocupa título, bordas, cabeçalho, separador e legenda (6 linhas), além de uma linha por idioma.
        EscreverPainel(textos.GetValueOrDefault("TituloSobre", "About"), LerSobre(idiomas[selecionado].Codigo), 0, idiomas.Count + 6, false, null);
    }

    Desenhar();

    while (true)
    {
        switch (LerTecla(Desenhar).Key)
        {
            case ConsoleKey.Escape: return null;
            case ConsoleKey.Enter or ConsoleKey.RightArrow:
                SalvarIdioma(idiomas[selecionado].Codigo);
                return idiomas[selecionado].Codigo;
            case ConsoleKey.UpArrow: selecionado = (selecionado - 1 + idiomas.Count) % idiomas.Count; break;
            case ConsoleKey.DownArrow: selecionado = (selecionado + 1) % idiomas.Count; break;
            default: continue;
        }

        Desenhar();
    }
}

// Textos da interface, lidos de UI_<idioma>.txt no formato Chave=Valor.
Dictionary<string, string> LerTextos(string idioma)
{
    var caminho = CaminhoDe($"UI_{idioma}.txt");

    if (!File.Exists(caminho))
        return [];

    return File.ReadAllLines(caminho)
        .Select(linha => linha.Split('=', 2))
        .Where(partes => partes.Length == 2)
        .ToDictionary(partes => partes[0].Trim(), partes => partes[1].Trim());
}

// Quebra as linhas do arquivo na largura útil do painel, para que cada item seja exatamente uma linha na tela.
List<string> QuebrarLinhas(string[] linhas, int largura)
{
    var resultado = new List<string>();

    foreach (var linha in linhas)
    {
        if (linha.Length == 0)
            resultado.Add("");

        for (var i = 0; i < linha.Length; i += largura)
            resultado.Add(linha.Substring(i, Math.Min(largura, linha.Length - i)));
    }

    return resultado;
}

// Desenha um painel que ocupa o restante da janela abaixo de um bloco de alturaAcima linhas,
// mostrando o conteúdo a partir da linha deslocamento. Devolve o deslocamento ajustado aos limites.
// Sem formatoLinhas, o título não indica a faixa de linhas visível.
int EscreverPainel(string titulo, string[] conteudo, int deslocamento, int alturaAcima, bool emFoco, string? formatoLinhas)
{
    // Deixa uma linha livre no fim para não rolar a tela.
    var alturaPainel = Math.Max(3, Console.WindowHeight - alturaAcima - 1);

    // Desconta as bordas (2 linhas) e as bordas mais o espaçamento lateral (4 colunas).
    var linhasVisiveis = alturaPainel - 2;
    var linhas = QuebrarLinhas(conteudo, Math.Max(1, Console.WindowWidth - 4));

    deslocamento = Math.Clamp(deslocamento, 0, Math.Max(0, linhas.Count - linhasVisiveis));

    var trecho = linhas.Skip(deslocamento).Take(linhasVisiveis);
    var fim = Math.Min(linhas.Count, deslocamento + linhasVisiveis);

    var cabecalho = $"[bold]{Markup.Escape(titulo)}[/]";

    if (formatoLinhas is not null && linhas.Count > linhasVisiveis)
        cabecalho += $" [grey]({Markup.Escape(string.Format(formatoLinhas, deslocamento + 1, fim, linhas.Count))})[/]";

    AnsiConsole.Write(new Panel(new Text(string.Join('\n', trecho))) { Height = alturaPainel }
        .Header(cabecalho)
        .Border(BoxBorder.Rounded)
        .BorderColor(emFoco ? Color.Yellow : Color.Grey)
        .Expand());

    return deslocamento;
}

// Texto do About no idioma indicado, com o inglês como alternativa quando a tradução não existe.
// {versao} é trocado pela versão do executável, definida só no .csproj.
string[] LerSobre(string idioma)
{
    var versao = Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "?";

    foreach (var arquivo in new[] { $"ABOUT_{idioma}.txt", "ABOUT_ENG.txt" })
    {
        var caminho = CaminhoDe(arquivo);

        if (File.Exists(caminho))
            return File.ReadAllLines(caminho).Select(linha => linha.Replace("{versao}", versao)).ToArray();
    }

    return [];
}

// Minúsculas e sem acentos, para "Reator", "REATOR" e "reátor" darem o mesmo resultado na pesquisa.
string Normalizar(string texto)
{
    var decomposto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
    var resultado = new StringBuilder(decomposto.Length);

    foreach (var caractere in decomposto)
    {
        if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            resultado.Append(caractere);
    }

    return resultado.ToString().Normalize(NormalizationForm.FormC);
}

// Pontua cada tópico pelos termos da consulta: o título vale mais que as palavras-chave, que valem mais que o
// texto. Tópicos que contêm todos os termos vêm primeiro. Cada resultado aponta a linha com mais termos.
List<Resultado> BuscarTopicos(List<Topico> topicos, string consulta)
{
    var termos = Normalizar(consulta).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var resultados = new List<Resultado>();

    if (termos.Length == 0)
        return resultados;

    foreach (var topico in topicos)
    {
        var pontos = 0;
        var encontrados = 0;

        foreach (var termo in termos)
        {
            if (topico.TituloNormalizado.Contains(termo))
                pontos += 6;
            else if (topico.PalavrasChave.Contains(termo))
                pontos += 4;
            else if (topico.LinhasNormalizadas.Any(linha => linha.Contains(termo)))
                pontos += 1;
            else
                continue;

            encontrados++;
        }

        if (encontrados == 0)
            continue;

        if (encontrados == termos.Length)
            pontos += 100;

        var melhorLinha = -1;
        var melhorContagem = 0;

        for (var i = 0; i < topico.LinhasNormalizadas.Length; i++)
        {
            var contagem = termos.Count(termo => topico.LinhasNormalizadas[i].Contains(termo));

            if (contagem > melhorContagem)
            {
                melhorContagem = contagem;
                melhorLinha = i;
            }
        }

        resultados.Add(new Resultado(topico, melhorLinha, pontos + melhorContagem));
    }

    return resultados.OrderByDescending(resultado => resultado.Pontos).ThenBy(resultado => resultado.Topico.Titulo).ToList();
}

// Tela principal. Devolve true quando o usuário pede a troca de idioma (F8) e false quando confirma a saída.
bool ExibirTelas(string idioma)
{
    var textos = LerTextos(idioma);
    string TextoBruto(string chave) => textos.GetValueOrDefault(chave, chave);
    string Texto(string chave) => Markup.Escape(TextoBruto(chave));

    var procedimentos = LerProcedimentos(idioma);

    // As duas listas mostram o mesmo número de linhas; a de procedimentos rola por dentro quando tem mais itens.
    var linhasLista = telas.Length;

    var ativa = Fonte.Telas;        // O que alimenta o painel: uma das duas listas ou a calculadora.
    var focoNoPainel = false;       // Nas listas, se as setas rolam o painel em vez de mudar a seleção.

    // Onde o usuário estava ao abrir a calculadora com F7, para Esc ou F7 voltarem exatamente para lá.
    var ativaAntesDaCalculadora = Fonte.Telas;
    var focoAntesDaCalculadora = false;
    var telaSelecionada = 0;
    var procedimentoSelecionado = 0;
    var inicioProcedimentos = 0;    // Primeiro procedimento visível na lista.
    var deslocamento = 0;
    var conteudo = Carregar();

    // Calculadora: as posições 0 a 3 são os campos (nave X/Y, scan X/Y); 4 e 5 são as linhas PLOT ROUTE e WARP.
    var posicao = 0;
    var comandoSelecionado = 0;
    var mensagem = "";              // Resultado da última cópia, já em markup.
    var confirmandoSaida = false;   // Depois do primeiro Esc, aguardando o segundo para sair.

    // Pesquisa livre (F9 ou Ctrl+F): o índice reúne as telas e os procedimentos do idioma.
    var topicos = MontarIndice();
    var consulta = "";
    var resultados = new List<Resultado>();
    var resultadoSelecionado = 0;
    var inicioResultados = 0;       // Primeiro resultado visível na lista.
    var ativaAntesDaPesquisa = Fonte.Telas;
    var focoAntesDaPesquisa = false;

    // Linhas iniciadas com # são metadados (como as palavras-chave da pesquisa) e não são exibidas.
    string[] LerArquivo(string arquivo)
    {
        var caminho = CaminhoDe(arquivo);

        return File.Exists(caminho)
            ? File.ReadAllLines(caminho).Where(linha => !linha.StartsWith('#')).ToArray()
            : [string.Format(textos.GetValueOrDefault("NaoEncontrado", "{0}"), caminho)];
    }

    List<Topico> MontarIndice()
    {
        var lista = new List<Topico>();

        for (var i = 0; i < telas.Length; i++)
            lista.Add(CriarTopico($"{telas[i].Nome} ({telas[i].Tecla})", Fonte.Telas, i, $"{telas[i].Nome}_{idioma}.txt"));

        for (var i = 0; i < procedimentos.Count; i++)
            lista.Add(CriarTopico(procedimentos[i].Titulo, Fonte.Procedimentos, i,
                Path.Combine("Procedures", $"{procedimentos[i].Id}_{idioma}.txt")));

        return lista;
    }

    Topico CriarTopico(string titulo, Fonte fonte, int indice, string arquivo)
    {
        const string Prefixo = "# keywords:";
        var caminho = CaminhoDe(arquivo);
        var brutas = File.Exists(caminho) ? File.ReadAllLines(caminho) : [];
        var chaves = string.Join(' ', brutas.Where(linha => linha.StartsWith(Prefixo)).Select(linha => linha[Prefixo.Length..]));
        var linhas = brutas.Where(linha => !linha.StartsWith('#')).ToArray();

        return new Topico(titulo, fonte, indice, linhas, linhas.Select(Normalizar).ToArray(), Normalizar(titulo), Normalizar(chaves));
    }

    void Pesquisar()
    {
        resultados = BuscarTopicos(topicos, consulta);
        resultadoSelecionado = 0;
        inicioResultados = 0;
    }

    // Abre o resultado escolhido na tela normal, já rolado até a linha encontrada (com uma linha de contexto).
    void AbrirResultado()
    {
        if (resultados.Count == 0)
            return;

        var resultado = resultados[resultadoSelecionado];
        ativa = resultado.Topico.Fonte;

        if (ativa == Fonte.Telas)
        {
            telaSelecionada = resultado.Topico.Indice;
            Atualizar();
        }
        else
        {
            SelecionarProcedimento(resultado.Topico.Indice);
        }

        focoNoPainel = true;

        if (resultado.Linha > 0)
        {
            var anteriores = QuebrarLinhas(conteudo.Take(resultado.Linha).ToArray(), Math.Max(1, Console.WindowWidth - 4));
            deslocamento = Math.Max(0, anteriores.Count - 1);
        }
    }

    // Devolve se a tecla teve efeito, para a tela só ser redesenhada quando necessário.
    bool TratarPesquisa(ConsoleKeyInfo tecla)
    {
        switch (tecla.Key)
        {
            case ConsoleKey.UpArrow when resultados.Count > 0:
                resultadoSelecionado = (resultadoSelecionado - 1 + resultados.Count) % resultados.Count;
                break;
            case ConsoleKey.DownArrow when resultados.Count > 0:
                resultadoSelecionado = (resultadoSelecionado + 1) % resultados.Count;
                break;
            case ConsoleKey.Enter:
                AbrirResultado();
                break;
            case ConsoleKey.Backspace when consulta.Length > 0:
                consulta = consulta[..^1];
                Pesquisar();
                break;
            case ConsoleKey.Delete:
                consulta = "";
                Pesquisar();
                break;
            default:
                if (char.IsControl(tecla.KeyChar) || consulta.Length >= 40)
                    return false;

                consulta += tecla.KeyChar;
                Pesquisar();
                break;
        }

        return true;
    }

    string[] Carregar() => ativa == Fonte.Procedimentos
        ? LerArquivo(Path.Combine("Procedures", $"{procedimentos[procedimentoSelecionado].Id}_{idioma}.txt"))
        : LerArquivo($"{telas[telaSelecionada].Nome}_{idioma}.txt");

    void Atualizar()
    {
        conteudo = Carregar();
        deslocamento = 0;
    }

    void SelecionarProcedimento(int indice)
    {
        procedimentoSelecionado = indice;

        // Mantém o procedimento selecionado dentro da janela visível da lista.
        inicioProcedimentos = Math.Clamp(inicioProcedimentos, indice - linhasLista + 1, indice);
        Atualizar();
    }

    // Destaque do item selecionado: amarelo na lista com o foco, cinza claro na lista que alimenta o painel
    // enquanto se rola o conteúdo, e cinza escuro na outra lista, só para lembrar a última escolha.
    string Estilo(bool selecionado, bool listaAtiva, bool listaComFoco) =>
        !selecionado ? "default"
        : !listaAtiva ? "white on grey23"
        : listaComFoco ? "black on yellow"
        : "black on grey";

    // Corta o texto que não cabe na coluna, para a tabela não ganhar linhas extras.
    static string Cortar(string texto, int largura) =>
        texto.Length <= largura ? texto : texto[..Math.Max(0, largura - 1)] + "…";

    Grid MontarListas()
    {
        var larguraTelas = Console.WindowWidth / 2;
        var larguraProcedimentos = Console.WindowWidth - larguraTelas;
        var focoTelas = ativa == Fonte.Telas && !focoNoPainel;
        var focoProcedimentos = ativa == Fonte.Procedimentos && !focoNoPainel;

        var tabelaTelas = new Table { Width = larguraTelas }
            .Expand()
            .Border(TableBorder.Rounded)
            .BorderColor(focoTelas ? Color.Yellow : Color.Grey)
            .Title($"[bold]{Texto("TituloReferencia")}[/]")
            .AddColumn($"[bold]{Texto("ColunaTecla")}[/]")
            .AddColumn($"[bold]{Texto("ColunaTela")}[/]");

        for (var i = 0; i < telas.Length; i++)
        {
            var estilo = Estilo(i == telaSelecionada, ativa == Fonte.Telas, focoTelas);
            tabelaTelas.AddRow($"[{estilo}]{telas[i].Tecla}[/]", $"[{estilo}]{telas[i].Nome}[/]");
        }

        // Quando há mais procedimentos do que linhas, o cabeçalho indica a faixa visível.
        var cabecalho = $"[bold]{Texto("ColunaObjetivo")}[/]";
        var fimVisivel = Math.Min(procedimentos.Count, inicioProcedimentos + linhasLista);

        if (procedimentos.Count > linhasLista)
            cabecalho += $" [grey]{inicioProcedimentos + 1}–{fimVisivel}/{procedimentos.Count}[/]";

        var tabelaProcedimentos = new Table { Width = larguraProcedimentos }
            .Expand()
            .Border(TableBorder.Rounded)
            .BorderColor(focoProcedimentos ? Color.Yellow : Color.Grey)
            .Title($"[bold]{Texto("TituloProcedimentos")}[/]")
            .AddColumn(cabecalho);

        // Desconta as bordas e o espaçamento lateral (4 colunas).
        var larguraTexto = Math.Max(1, larguraProcedimentos - 4);

        for (var i = inicioProcedimentos; i < inicioProcedimentos + linhasLista; i++)
        {
            if (i >= procedimentos.Count)
            {
                tabelaProcedimentos.AddRow("");
                continue;
            }

            var estilo = Estilo(i == procedimentoSelecionado, ativa == Fonte.Procedimentos, focoProcedimentos);
            tabelaProcedimentos.AddRow($"[{estilo}]{Markup.Escape(Cortar(procedimentos[i].Titulo, larguraTexto))}[/]");
        }

        var grade = new Grid()
            .AddColumn(new GridColumn().Padding(0, 0, 0, 0))
            .AddColumn(new GridColumn().Padding(0, 0, 0, 0));

        return grade.AddRow(tabelaTelas, tabelaProcedimentos);
    }

    // Soma as coordenadas da nave e as relativas do scan. O jogo espera ponto decimal, por isso a cultura
    // invariante: num Windows em português o padrão seria vírgula, o que daria erro de sintaxe no jogo.
    string? Destino()
    {
        var valores = new double[4];

        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(camposCalculadora[i], NumberStyles.Float, CultureInfo.InvariantCulture, out valores[i]))
                return null;
        }

        // Somar 0.0 evita que um resultado zero seja exibido como "-0".
        static string Formatar(double valor) => (Math.Round(valor, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture);

        return $"{Formatar(valores[0] + valores[2])},{Formatar(valores[1] + valores[3])}";
    }

    string[] Comandos() => Destino() is { } destino
        ? [$"PLOT ROUTE {destino}", $"WARP {destino}"]
        : ["PLOT ROUTE ?", "WARP ?"];

    void Copiar()
    {
        if (Destino() is null)
        {
            mensagem = $"[red]{Texto("CalcPreencha")}[/]";
            return;
        }

        var comando = Comandos()[comandoSelecionado];

        mensagem = CopiarParaAreaDeTransferencia(comando)
            ? $"[green]{Markup.Escape(string.Format(TextoBruto("CalcCopiado"), comando))}[/]"
            : $"[red]{Texto("CalcErroCopia")}[/]";
    }

    // Aceita algarismos, ponto ou vírgula (ambos viram ponto) e o sinal de menos, que alterna o sinal do número.
    bool Digitar(char caractere)
    {
        var campo = camposCalculadora[posicao];

        if (char.IsDigit(caractere) && campo.Length < 12)
            camposCalculadora[posicao] = campo + caractere;
        else if (caractere is '.' or ',' && !campo.Contains('.'))
            camposCalculadora[posicao] = (campo is "" or "-" ? campo + "0" : campo) + ".";
        else if (caractere == '-')
            camposCalculadora[posicao] = campo.StartsWith('-') ? campo[1..] : "-" + campo;
        else
            return false;

        return true;
    }

    // Devolve se a tecla teve efeito, para a tela só ser redesenhada quando necessário.
    bool TratarCalculadora(ConsoleKeyInfo tecla)
    {
        var emCampo = posicao < 4;

        switch (tecla.Key)
        {
            case ConsoleKey.UpArrow:
                posicao = posicao switch { 0 or 1 => posicao, 2 or 3 => posicao - 2, 4 => 2, _ => 4 };
                break;
            case ConsoleKey.DownArrow:
                posicao = posicao switch { 0 or 1 => posicao + 2, 2 or 3 => 4, _ => 5 };
                break;
            case ConsoleKey.LeftArrow when emCampo:
                posicao -= posicao % 2;
                break;
            case ConsoleKey.RightArrow when emCampo:
                posicao += 1 - posicao % 2;
                break;
            case ConsoleKey.Enter:
                Copiar();
                return true;
            case ConsoleKey.Backspace when emCampo && camposCalculadora[posicao].Length > 0:
                camposCalculadora[posicao] = camposCalculadora[posicao][..^1];
                break;
            case ConsoleKey.Delete when emCampo:
                camposCalculadora[posicao] = "";
                break;
            default:
                if (!emCampo || !Digitar(tecla.KeyChar))
                    return false;
                break;
        }

        if (posicao >= 4)
            comandoSelecionado = posicao - 4;

        mensagem = "";
        return true;
    }

    void EscreverCalculadora(int alturaAcima)
    {
        // Deixa uma linha livre no fim para não rolar a tela.
        var alturaPainel = Math.Max(3, Console.WindowHeight - alturaAcima - 1);

        string Campo(int indice)
        {
            var texto = Markup.Escape(camposCalculadora[indice].PadRight(12));
            return indice == posicao ? $"[black on yellow]{texto}[/]" : texto;
        }

        var rotulos = new[] { TextoBruto("CalcNave"), TextoBruto("CalcScan"), TextoBruto("CalcDestino") };
        var larguraRotulo = rotulos.Max(rotulo => rotulo.Length) + 3;
        string Rotulo(int indice) => Markup.Escape(rotulos[indice].PadRight(larguraRotulo));

        var destino = Destino();
        var linhas = new List<string>
        {
            "",
            $"{Rotulo(0)}X [[{Campo(0)}]]   Y [[{Campo(1)}]]",
            $"{Rotulo(1)}X [[{Campo(2)}]]   Y [[{Campo(3)}]]",
            "",
            destino is null
                ? $"{Rotulo(2)}[grey]{Texto("CalcPreencha")}[/]"
                : $"{Rotulo(2)}[bold]{Markup.Escape(destino)}[/]",
            "",
        };

        // O comando escolhido para copiar é marcado com ▶; o que está sob o cursor fica destacado.
        var comandos = Comandos();

        for (var i = 0; i < comandos.Length; i++)
        {
            var marca = i == comandoSelecionado ? "▶ " : "  ";
            var texto = Markup.Escape(comandos[i]);
            linhas.Add(posicao == 4 + i ? $"{marca}[black on yellow]{texto}[/]" : $"{marca}{texto}");
        }

        linhas.Add("");
        linhas.Add(mensagem);

        AnsiConsole.Write(new Panel(new Markup(string.Join('\n', linhas))) { Height = alturaPainel }
            .Header($"[bold]{Texto("TituloCalculadora")}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Expand());
    }

    void EscreverPesquisa(int alturaAcima)
    {
        // Deixa uma linha livre no fim para não rolar a tela.
        var alturaPainel = Math.Max(3, Console.WindowHeight - alturaAcima - 1);
        var larguraTexto = Math.Max(10, Console.WindowWidth - 4);

        var linhas = new List<string> { $"[yellow]>[/] {Markup.Escape(consulta)}[yellow]_[/]", "" };

        if (consulta.Trim().Length == 0)
        {
            linhas.Add($"[grey]{Texto("PesquisaDica")}[/]");
        }
        else if (resultados.Count == 0)
        {
            linhas.Add($"[grey]{Texto("PesquisaNada")}[/]");
        }
        else
        {
            linhas.Add($"[grey]{Markup.Escape(string.Format(TextoBruto("PesquisaResultados"), resultados.Count))}[/]");

            // Desconta as bordas (2 linhas) e as 3 linhas acima da lista; a lista rola para manter a seleção visível.
            var visiveis = Math.Max(1, alturaPainel - 5);
            inicioResultados = Math.Clamp(inicioResultados, resultadoSelecionado - visiveis + 1, resultadoSelecionado);

            for (var i = inicioResultados; i < Math.Min(resultados.Count, inicioResultados + visiveis); i++)
            {
                // Cada resultado mostra o tópico e a linha encontrada, com os espaços de alinhamento reduzidos.
                var resultado = resultados[i];
                var trecho = resultado.Linha >= 0
                    ? Regex.Replace(resultado.Topico.Linhas[resultado.Linha].Trim(), @"\s{2,}", "  ")
                    : "";
                var texto = Cortar(trecho.Length > 0 ? $"{resultado.Topico.Titulo} — {trecho}" : resultado.Topico.Titulo,
                    larguraTexto - 2);

                linhas.Add(i == resultadoSelecionado
                    ? $"▶ [black on yellow]{Markup.Escape(texto)}[/]"
                    : $"  {Markup.Escape(texto)}");
            }
        }

        AnsiConsole.Write(new Panel(new Markup(string.Join('\n', linhas))) { Height = alturaPainel }
            .Header($"[bold]{Texto("TituloPesquisa")}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Expand());
    }

    void Desenhar()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(MontarListas());

        var chaveLegenda = confirmandoSaida ? "ConfirmarSaida"
            : ativa == Fonte.Calculadora ? "LegendaCalculadora"
            : ativa == Fonte.Pesquisa ? "LegendaPesquisa"
            : focoNoPainel ? "LegendaPainel"
            : "LegendaMenu";
        var legenda = textos.GetValueOrDefault(chaveLegenda, "");
        var margem = new string(' ', Math.Max(0, (Console.WindowWidth - legenda.Length) / 2));
        var cor = confirmandoSaida ? "yellow" : "grey";
        AnsiConsole.MarkupLine($"{margem}[{cor}]{Markup.Escape(legenda)}[/]");

        // Título, bordas, cabeçalho e separador das listas (5 linhas), mais as linhas de itens e as da legenda.
        var linhasLegenda = Math.Max(1, (int)Math.Ceiling(legenda.Length / (double)Console.WindowWidth));
        var alturaAcima = linhasLista + 5 + linhasLegenda;

        if (ativa == Fonte.Calculadora)
        {
            EscreverCalculadora(alturaAcima);
            return;
        }

        if (ativa == Fonte.Pesquisa)
        {
            EscreverPesquisa(alturaAcima);
            return;
        }

        var titulo = ativa == Fonte.Procedimentos ? procedimentos[procedimentoSelecionado].Titulo : telas[telaSelecionada].Nome;

        deslocamento = EscreverPainel(titulo, conteudo, deslocamento, alturaAcima,
            focoNoPainel, textos.GetValueOrDefault("Linhas", "{0}–{1} / {2}"));
    }

    Desenhar();

    while (true)
    {
        var info = LerTecla(Desenhar);
        var tecla = info.Key;

        // Esc na tela principal pede confirmação: um segundo Esc sai, qualquer outra tecla só cancela.
        if (confirmandoSaida)
        {
            if (tecla == ConsoleKey.Escape)
                return false;

            confirmandoSaida = false;
            Desenhar();
            continue;
        }

        var indice = Array.FindIndex(telas, t => t.Tecla == tecla);

        // F9 e Ctrl+F abrem a pesquisa; a mesma tecla, ou Esc, volta para onde o usuário estava.
        var teclaPesquisa = tecla == ConsoleKey.F9
            || (tecla == ConsoleKey.F && info.Modifiers.HasFlag(ConsoleModifiers.Control));

        if (tecla == ConsoleKey.F8)
        {
            return true;
        }
        else if (teclaPesquisa && ativa != Fonte.Pesquisa)
        {
            ativaAntesDaPesquisa = ativa;
            focoAntesDaPesquisa = focoNoPainel;
            ativa = Fonte.Pesquisa;
        }
        else if (ativa == Fonte.Pesquisa && (teclaPesquisa || tecla == ConsoleKey.Escape))
        {
            ativa = ativaAntesDaPesquisa;
            focoNoPainel = focoAntesDaPesquisa;
        }
        else if (ativa == Fonte.Calculadora && tecla is ConsoleKey.Escape or ConsoleKey.F7)
        {
            // A calculadora é um nível acima da tela principal: Esc ou F7 voltam para onde o usuário estava.
            ativa = ativaAntesDaCalculadora;
            focoNoPainel = focoAntesDaCalculadora;
        }
        else if (tecla == ConsoleKey.Escape)
        {
            confirmandoSaida = true;
        }
        else if (indice >= 0)
        {
            // F1–F6 abrem a tela direto, de qualquer lugar.
            ativa = Fonte.Telas;
            telaSelecionada = indice;
            focoNoPainel = true;
            Atualizar();
        }
        else if (tecla == ConsoleKey.F7)
        {
            ativaAntesDaCalculadora = ativa;
            focoAntesDaCalculadora = focoNoPainel;
            ativa = Fonte.Calculadora;
            mensagem = "";
        }
        else if (ativa == Fonte.Calculadora)
        {
            // Na calculadora, o Tab não tem função e é ignorado junto com as demais teclas sem efeito.
            if (!TratarCalculadora(info))
                continue;
        }
        else if (ativa == Fonte.Pesquisa)
        {
            if (!TratarPesquisa(info))
                continue;
        }
        else if (tecla == ConsoleKey.Tab)
        {
            // Tab alterna só entre as duas listas; a calculadora fica no F7.
            if (procedimentos.Count == 0)
                continue;

            ativa = ativa == Fonte.Telas ? Fonte.Procedimentos : Fonte.Telas;
            focoNoPainel = false;
            Atualizar();
        }
        else if (focoNoPainel)
        {
            switch (tecla)
            {
                case ConsoleKey.UpArrow: deslocamento--; break;
                case ConsoleKey.DownArrow: deslocamento++; break;
                case ConsoleKey.LeftArrow: focoNoPainel = false; break;
                default: continue;
            }
        }
        else if (ativa == Fonte.Telas)
        {
            switch (tecla)
            {
                case ConsoleKey.UpArrow: telaSelecionada = (telaSelecionada - 1 + telas.Length) % telas.Length; Atualizar(); break;
                case ConsoleKey.DownArrow: telaSelecionada = (telaSelecionada + 1) % telas.Length; Atualizar(); break;
                case ConsoleKey.RightArrow or ConsoleKey.Enter: focoNoPainel = true; break;
                default: continue;
            }
        }
        else
        {
            switch (tecla)
            {
                case ConsoleKey.UpArrow: SelecionarProcedimento((procedimentoSelecionado - 1 + procedimentos.Count) % procedimentos.Count); break;
                case ConsoleKey.DownArrow: SelecionarProcedimento((procedimentoSelecionado + 1) % procedimentos.Count); break;
                case ConsoleKey.RightArrow or ConsoleKey.Enter: focoNoPainel = true; break;
                default: continue;
            }
        }

        Desenhar();
    }
}

// O que alimenta o painel de baixo na tela de referência.
enum Fonte { Telas, Procedimentos, Calculadora, Pesquisa }

// Um item pesquisável: uma tela de referência ou um procedimento, com o texto já normalizado para a busca.
record Topico(string Titulo, Fonte Fonte, int Indice, string[] Linhas, string[] LinhasNormalizadas,
    string TituloNormalizado, string PalavrasChave);

// Um tópico encontrado, a linha que melhor corresponde à consulta (-1 quando só o título ou as palavras-chave
// correspondem) e a pontuação usada para ordenar.
record Resultado(Topico Topico, int Linha, int Pontos);
