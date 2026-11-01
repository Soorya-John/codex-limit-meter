// Protocol fixture only. Never contacts Codex or OpenAI.
using System;
using System.Web.Script.Serialization;
using System.Collections.Generic;
class FakeCodex
{
    static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "app-server") return 1;
        var json = new JavaScriptSerializer();
        var init = json.Deserialize<Dictionary<string, object>>(Console.ReadLine());
        if ((string)init["method"] != "initialize") return 2;
        Console.WriteLine("{\"id\":1,\"result\":{}}");
        var ack = json.Deserialize<Dictionary<string, object>>(Console.ReadLine());
        if ((string)ack["method"] != "initialized") return 3;
        var read = json.Deserialize<Dictionary<string, object>>(Console.ReadLine());
        if ((string)read["method"] != "account/rateLimits/read") return 4;
        Console.Error.WriteLine("fixture diagnostic");
        // Ensure the client ignores unrelated notifications.
        Console.WriteLine("{\"method\":\"account/updated\",\"params\":{}}");
        if (Environment.GetEnvironmentVariable("METER_TEST_ERROR") == "1")
            Console.WriteLine("{\"id\":2,\"error\":{\"code\":-1,\"message\":\"fixture failure\"}}");
        else
            Console.WriteLine("{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":1800000000},\"secondary\":{\"usedPercent\":60,\"windowDurationMins\":10080}}}}}");
        return 0;
    }
}
