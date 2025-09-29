using subtitle_downloader.downloader;

namespace subtitle_downloader.test; 

public class NameParserTest {
    private static uint okResults, failResults;

    public static void runTests() {
        runOccurrencesTest(4, "title.....2005......g.g.", '.');
        runOccurrencesTest(3, "title.2005.gg.gg", '.');
        runOccurrencesTest(2, "title--2005-gggg", '-');
        runOccurrencesTest(0, "title 2005 gggg", '.');

        runGetYearTest(true, 2005, "2005");
        runGetYearTest(false, 0, "(205)");
        runGetYearTest(false, 0, "-2005");

        runMetadataParseTest(new Metadata {
            name = "Batman The Movie",
            year = 1966,
            releaseType = "BluRay"
        }, "Batman.The.Movie.1966.720p.BluRay.x264-CiNEFiLE");
        
        
        printResults();
    }

    private static void runMetadataParseTest(Metadata expected, string text) {
        Metadata actual = NameParser.parse(text);
        if (expected.Equals(actual)) {
            okResults++;
            return;
        }
        failResults++;
        Console.WriteLine("FAIL: expected=" + expected + " actual=" + actual + " for input=" + text);
    }
    
    private static void runOccurrencesTest(uint expected, string text, char target) {
        uint actual = NameParser.countSeparateOccurrences(text, target);
        if (expected == actual) {
            okResults++;
            return;
        }
        failResults++;
        Console.WriteLine("FAIL: expected=" + expected + " actual=" + actual + " for input=" + text + " target=" + target);
    }
    
    private static void runGetYearTest(bool expectedSuccess, uint expectedYear, string text) {
        var (success, year) = NameParser.getYear(text);
        if (success == expectedSuccess && year == expectedYear) {
            okResults++;
            return;
        }
        failResults++;
        Console.WriteLine("FAIL: expected=" + expectedSuccess + "," + expectedYear + 
                          " actual=" + success + "," + year + " for input=" + text);
    }
    
    private static void printResults() {
        Console.WriteLine("RESULTS");
        Console.WriteLine("  success: " + okResults);
        if (failResults != 0) {
            Console.WriteLine("  failed:  " + failResults);
            Console.WriteLine("  ----------");
            Console.WriteLine("  total:   " + (okResults + failResults));
        }
    }
}