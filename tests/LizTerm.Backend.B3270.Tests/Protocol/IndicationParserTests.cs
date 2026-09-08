using LizTerm.Backend.B3270.Protocol;

namespace LizTerm.Backend.B3270.Tests.Protocol;

public class IndicationParserTests
{
    private static T Parse<T>(string line) where T : Indication
    {
        Assert.True(IndicationParser.TryParse(line, out var indication), "parse failed");
        return Assert.IsType<T>(indication);
    }

    [Fact]
    public void Parses_initialize_with_nested_hello_and_screen_mode()
    {
        var line = """{"initialize":[{"hello":{"version":"4.5.6","build":"b3270 v4.5ga6 Mon Jul 27 22:37:48 UTC 2026 brew","copyright":"..."}},{"screen-mode":{"model":4,"rows":43,"columns":80,"color":true,"oversize":false,"extended":true}},{"setting":{"name":"codePage","value":"bracket","cause":"none"}}]}""";
        var init = Parse<InitializeIndication>(line);
        Assert.Equal(3, init.Items.Count);
        var hello = Assert.IsType<HelloIndication>(init.Items[0]);
        Assert.Equal("4.5.6", hello.Version);
        var mode = Assert.IsType<ScreenModeIndication>(init.Items[1]);
        Assert.Equal(43, mode.Rows);
        var setting = Assert.IsType<SettingIndication>(init.Items[2]);
        Assert.Equal("codePage", setting.Name);
        Assert.Equal("bracket", setting.Value);
    }

    [Fact]
    public void Parses_screen_mode()
    {
        var mode = Parse<ScreenModeIndication>("""{"screen-mode":{"model":2,"rows":24,"columns":80,"color":true,"oversize":false,"extended":true}}""");
        Assert.Equal(2, mode.Model);
        Assert.Equal(24, mode.Rows);
        Assert.Equal(80, mode.Columns);
        Assert.True(mode.Color);
        Assert.True(mode.Extended);
    }

    [Fact]
    public void Parses_erase()
    {
        var erase = Parse<EraseIndication>("""{"erase":{"logical-rows":43,"logical-columns":80,"fg":"blue","bg":"neutralBlack"}}""");
        Assert.Equal(43, erase.LogicalRows);
        Assert.Equal("blue", erase.Fg);
        Assert.Equal("neutralBlack", erase.Bg);
        var bare = Parse<EraseIndication>("""{"erase":{}}""");
        Assert.Null(bare.LogicalRows);
        Assert.Null(bare.Fg);
    }

    [Fact]
    public void Parses_screen_with_rows_and_cursor()
    {
        var line = """{"screen":{"cursor":{"enabled":true,"row":2,"column":9},"rows":[{"row":1,"changes":[{"column":2,"fg":"red","count":1},{"column":3,"fg":"neutralBlack","bg":"red","text":"_______________________________________________"},{"column":50,"fg":"neutralBlack","bg":"red","count":31}]},{"row":2,"changes":[{"column":2,"text":"Field:"},{"column":8,"fg":"red","count":73}]}]}}""";
        var screen = Parse<ScreenIndication>(line);
        Assert.NotNull(screen.Cursor);
        Assert.True(screen.Cursor!.Enabled);
        Assert.Equal(2, screen.Cursor.Row);
        Assert.Equal(9, screen.Cursor.Column);
        Assert.NotNull(screen.Rows);
        Assert.Equal(2, screen.Rows!.Count);
        var row1 = screen.Rows[0];
        Assert.Equal(1, row1.Row);
        Assert.Equal(3, row1.Changes.Count);
        Assert.Equal(2, row1.Changes[0].Column);
        Assert.Equal(1, row1.Changes[0].Count);
        Assert.Equal("red", row1.Changes[0].Fg);
        Assert.Null(row1.Changes[0].Text);
        Assert.Equal("neutralBlack", row1.Changes[1].Fg);
        Assert.Equal("red", row1.Changes[1].Bg);
        Assert.StartsWith("____", row1.Changes[1].Text);
        Assert.Null(row1.Changes[1].Gr);
        Assert.Equal("Field:", screen.Rows[1].Changes[0].Text);
    }

    [Fact]
    public void Parses_screen_with_only_cursor()
    {
        var screen = Parse<ScreenIndication>("""{"screen":{"cursor":{"enabled":false}}}""");
        Assert.NotNull(screen.Cursor);
        Assert.False(screen.Cursor!.Enabled);
        Assert.Null(screen.Cursor.Row);
        Assert.Null(screen.Rows);
    }

    [Fact]
    public void Parses_change_with_gr()
    {
        var screen = Parse<ScreenIndication>("""{"screen":{"rows":[{"row":7,"changes":[{"column":1,"fg":"neutralWhite","gr":"highlight,selectable","text":" Welcome to"}]}]}}""");
        Assert.Equal("highlight,selectable", screen.Rows![0].Changes[0].Gr);
    }

    [Fact]
    public void Parses_oia_with_and_without_value()
    {
        var lock1 = Parse<OiaIndication>("""{"oia":{"field":"lock","value":"not-connected"}}""");
        Assert.Equal("lock", lock1.Field);
        Assert.Equal("not-connected", lock1.Value);
        var lock2 = Parse<OiaIndication>("""{"oia":{"field":"lock"}}""");
        Assert.Null(lock2.Value);
        var insert = Parse<OiaIndication>("""{"oia":{"field":"insert","value":true}}""");
        Assert.Equal("true", insert.Value);
        var lu = Parse<OiaIndication>("""{"oia":{"field":"lu","value":"IBM0TEQO"}}""");
        Assert.Equal("IBM0TEQO", lu.Value);
    }

    [Fact]
    public void Parses_connection()
    {
        var c = Parse<ConnectionIndication>("""{"connection":{"state":"connected-tn3270e","host":"127.0.0.1","cause":"ui"}}""");
        Assert.Equal("connected-tn3270e", c.State);
        Assert.Equal("127.0.0.1", c.Host);
        var d = Parse<ConnectionIndication>("""{"connection":{"state":"not-connected"}}""");
        Assert.Equal("not-connected", d.State);
        Assert.Null(d.Host);
    }

    [Fact]
    public void Parses_tls()
    {
        var t = Parse<TlsIndication>("""{"tls":{"secure":true,"verified":false,"session":"Version: TLSv1.3\nCipher: TLS_AES_256_GCM_SHA384","host-cert":"Subject: CN = localhost"}}""");
        Assert.True(t.Secure);
        Assert.False(t.Verified);
        Assert.StartsWith("Version: TLSv1.3", t.Session);
        Assert.Equal("Subject: CN = localhost", t.HostCert);
        var off = Parse<TlsIndication>("""{"tls":{"secure":false}}""");
        Assert.False(off.Secure);
        Assert.Null(off.Verified);
    }

    [Fact]
    public void Parses_run_result_success_and_failure()
    {
        var ok = Parse<RunResultIndication>("""{"run-result":{"r-tag":"t1","success":true,"text":["3279-4-E"],"text-err":[false],"time":0}}""");
        Assert.Equal("t1", ok.Tag);
        Assert.True(ok.Success);
        Assert.Equal(["3279-4-E"], ok.Text);
        var bare = Parse<RunResultIndication>("""{"run-result":{"r-tag":"c","success":true,"time":0.784}}""");
        Assert.Empty(bare.Text);
        var fail = Parse<RunResultIndication>("""{"run-result":{"r-tag":"t4","success":false,"text":["Connection failed:","nonexistent.invalid/23:","nodename nor servname provided, or not known"],"text-err":[true,true,true],"time":0.039}}""");
        Assert.False(fail.Success);
        Assert.Equal(3, fail.Text.Count);
        Assert.True(fail.TextErr[0]);
    }

    [Fact]
    public void Parses_popup_and_ui_error()
    {
        var popup = Parse<PopupIndication>("""{"popup":{"type":"connection-error","text":"Host unreachable","retrying":false}}""");
        Assert.Equal("connection-error", popup.Type);
        Assert.Equal("Host unreachable", popup.Text);
        var err = Parse<UiErrorIndication>("""{"ui-error":{"fatal":false,"text":"Element 0: Not an object","operation":"run","member":"actions"}}""");
        Assert.False(err.Fatal);
        Assert.Equal("run", err.Operation);
    }

    [Fact]
    public void Parses_tls_hello()
    {
        var line = """{"tls-hello":{"supported":true,"provider":"OpenSSL 3.6.3 9 Jun 2026","options":["acceptHostname","verifyHostCert","startTls","caDir","caFile","certFile","certFileType","chainFile","keyFile","keyFileType","keyPasswd","tlsMinProtocol","tlsMaxProtocol","tlsSecurityLevel"]}}""";
        var hello = Parse<TlsHelloIndication>(line);
        Assert.True(hello.Supported);
        Assert.Equal("OpenSSL 3.6.3 9 Jun 2026", hello.Provider);
        Assert.Contains("caFile", hello.Options);
        Assert.Equal(14, hello.Options.Count);
    }

    /// <summary>A Schannel engine (or any provider without TLS compiled in) sends supported:false with no
    /// options at all; the parser must degrade to an empty list rather than throw.</summary>
    [Fact]
    public void Tls_hello_with_no_options_key_degrades_to_an_empty_list()
    {
        var hello = Parse<TlsHelloIndication>("""{"tls-hello":{"supported":false}}""");
        Assert.False(hello.Supported);
        Assert.Null(hello.Provider);
        Assert.Empty(hello.Options);
    }

    /// <summary>A malformed options value (not an array) must not throw either: StringList already degrades
    /// this the same way every other list field in the protocol does.</summary>
    [Fact]
    public void Tls_hello_with_malformed_options_degrades_to_an_empty_list()
    {
        var hello = Parse<TlsHelloIndication>("""{"tls-hello":{"supported":true,"options":"not-an-array"}}""");
        Assert.Empty(hello.Options);
    }

    [Fact]
    public void Parses_ft()
    {
        var ft = Parse<FtIndication>("""{"ft":{"state":"running","bytes":4096,"cause":"ui"}}""");
        Assert.Equal("running", ft.State);
        Assert.Equal(4096, ft.Bytes);
        var done = Parse<FtIndication>("""{"ft":{"state":"complete","success":true,"text":"Transfer complete, 12 bytes","cause":"ui"}}""");
        Assert.True(done.Success);
    }

    [Fact]
    public void Unknown_indications_are_reported_by_name()
    {
        var u = Parse<UnknownIndication>("""{"stats":{"bytes-received":107,"records-received":1}}""");
        Assert.Equal("stats", u.Name);
        Assert.IsType<UnknownIndication>(Parse<UnknownIndication>("""{"bell":{}}"""));
    }

    [Fact]
    public void Malformed_lines_return_false()
    {
        Assert.False(IndicationParser.TryParse("not json", out _));
        Assert.False(IndicationParser.TryParse("", out _));
        Assert.False(IndicationParser.TryParse("[1,2]", out _));
        Assert.False(IndicationParser.TryParse("{}", out _));
    }

    [Theory]
    [InlineData("""{"hello":"x"}""")]
    [InlineData("""{"screen-mode":123}""")]
    [InlineData("""{"oia":null}""")]
    [InlineData("""{"connection":[1,2,3]}""")]
    [InlineData("""{"initialize":{"hello":{}}}""")]
    public void Non_object_bodies_return_false(string line)
    {
        Assert.False(IndicationParser.TryParse(line, out _));
    }

    [Fact]
    public void Initialize_skips_non_object_items()
    {
        var line = """{"initialize":[1,{"hello":{"version":"4.5.6","build":"b"}},"x"]}""";
        var init = Parse<InitializeIndication>(line);
        Assert.Single(init.Items);
        var hello = Assert.IsType<HelloIndication>(init.Items[0]);
        Assert.Equal("4.5.6", hello.Version);
    }

    [Fact]
    public void Run_result_text_tolerates_non_string_entries()
    {
        var line = """{"run-result":{"r-tag":"1","success":true,"text":["ok",123,true,null]}}""";
        var result = Parse<RunResultIndication>(line);
        Assert.Equal(["ok", "123", "true", ""], result.Text);
    }

    [Fact]
    public void Indfile_fixture_parses_with_the_expected_ft_sequence()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "indfile-tso-roundtrip.jsonl");
        var states = new List<FtIndication>();
        foreach (var line in File.ReadLines(path))
        {
            Assert.True(IndicationParser.TryParse(line, out var indication), "unparsable line: " + line);
            if (indication is FtIndication ft) states.Add(ft);
        }
        // One send and one receive, each awaiting -> running... -> complete, both successful.
        Assert.NotEmpty(states);
        Assert.Equal("awaiting", states[0].State);
        Assert.Contains(states, s => s.State == "running" && s.Bytes > 0);
        var completes = states.Where(s => s.State == "complete").ToList();
        Assert.Equal(2, completes.Count);
        Assert.All(completes, c =>
        {
            Assert.True(c.Success);
            Assert.Contains("Transfer complete", c.Text);
        });
    }
}
