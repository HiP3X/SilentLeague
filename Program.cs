using System;
using Microsoft.Toolkit.Uwp.Notifications;
using LCUClient;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace silentLeague
{
    static class GlobalListener
    {
        public static LCUListener listener = new LCUListener();
    }
    static class globalControl
    {
        public static bool control = true;
        public static bool listnerControl = true;
        public static string tempStorage = null;
        public static string region;
    }
    static class team
    {
        public static string[] teamID = new string[5];
        public static string[] teamName = new string[5];
    }
    class Program
    {
        public class MyCustomApplicationContext : ApplicationContext
        {
            //public static  NotifyIcon trayIcon;
            public NotifyIcon trayIcon = new NotifyIcon();

            static async void hold() //holding until out of lobby
            {
                foreach (var LCU in GlobalListener.listener.GetGatheredLCUs())
                {
                    var response = await LCU.HttpGet("/lol-champ-select/v1/session");
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        Thread.Sleep(3000);
                        hold();
                    }
                    else
                    {
                        Lobby();
                    }
                }
            }
            static async void Lobby() //waiting for lobby
            {
                bool temp = true;
                while (temp)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        foreach (var LCU in GlobalListener.listener.GetGatheredLCUs())
                        {
                            var response = await LCU.HttpGet("/lol-champ-select/v1/session");
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                temp = false;
                                Thread.Sleep(500);
                                send();
                            }
                        }
                    }
                    catch
                    {
                        Thread.Sleep(1000);
                    }
                }
            }

            static async void send()
            {
                //getting current server
                foreach (var LCU in GlobalListener.listener.GetGatheredLCUs())
                {
                    var response = await LCU.HttpGet("/riotclient/get_region_locale");
                    var region = await response.Content.ReadAsStringAsync();
                    var temp = region.Substring(region.LastIndexOf("region") + 9);
                    var refionFinal = temp.Substring(0, temp.LastIndexOf("webLanguage") - 3);
                    refionFinal = refionFinal.ToLower();
                    globalControl.region = refionFinal;
                }

                Thread.Sleep(100);

                //getting players ID
                foreach (var LCU in GlobalListener.listener.GetGatheredLCUs())
                {
                    var responseID = await LCU.HttpGet("/lol-champ-select/v1/session");
                    var currentID = await responseID.Content.ReadAsStringAsync();

                    for (int i = 0; i < team.teamID.Length; i++)
                    {
                        if (string.IsNullOrEmpty(globalControl.tempStorage))
                        {
                            string output = currentID.Substring(currentID.IndexOf("\"summonerId\"") + 13);
                            globalControl.tempStorage = output;
                            string summonerID = globalControl.tempStorage.Substring(0, globalControl.tempStorage.IndexOf(","));
                            if (string.IsNullOrEmpty(summonerID))
                            {
                                break;
                            }
                            else
                            {
                                team.teamID[i] = summonerID;
                            }

                        }
                        else
                        {
                            string output = globalControl.tempStorage.Substring(globalControl.tempStorage.IndexOf("\"summonerId\"") + 13);
                            globalControl.tempStorage = output;
                            string summonerID = globalControl.tempStorage.Substring(0, globalControl.tempStorage.IndexOf(","));
                            if (string.IsNullOrEmpty(summonerID))
                            {
                                break;
                            }
                            else
                            {
                                team.teamID[i] = summonerID;
                            }
                        }
                    }
                }


                globalControl.tempStorage = null;

                //getting players names
                for (int i = 0; i < team.teamID.Length; i++)
                {
                    foreach (var LCU in GlobalListener.listener.GetGatheredLCUs())
                    {
                        var responseName = await LCU.HttpGet("/lol-summoner/v1/summoners/" + team.teamID[i]);
                        var currentName = await responseName.Content.ReadAsStringAsync();

                        string playerName = currentName.Substring(currentName.IndexOf("\"displayName\"") + 15);
                        string playerNameFinal = playerName.Substring(0, playerName.IndexOf("\""));
                        team.teamName[i] = playerNameFinal;
                    }
                }

                Thread.Sleep(100);

                Uri opgg = new Uri("https://" + globalControl.region + ".op.gg/multi/query=" + team.teamName[0] + ", " + team.teamName[1] + ", " + team.teamName[2] + ", " + team.teamName[3] + ", " + team.teamName[4]);
                //^ building the opgg url

                new ToastContentBuilder() //sends notification containing all players in the lobby
                    .AddArgument("action", "viewConversation")
                    .AddArgument("conversationId", 9813)
                    .AddText("Your OPGG Multi-Search is ready")
                    .AddText("Click here!")
                    .SetProtocolActivation(opgg)
                    //.AddAppLogoOverride(new Uri("ms-appdata:///local/logo.jpg"))//, ToastGenericAppLogoCrop.Circle) //need to figure this out so I can add a logo
                    .Show();
                hold();
            }

            public void listner() //listens for client
            {
                welcomeToast();
                //starting the listener and waiting for any client to appear
                GlobalListener.listener.WaitForAnyClient();
                Lobby();
            }

            static void welcomeToast()
            {
                new ToastContentBuilder() //sends ready notification
                    .AddArgument("action", "viewConversation")
                    .AddArgument("conversationId", 9813)
                    .AddText("Program started successfully!")
                    .AddText("Feel free to queue up")
                    .Show();
            }
            public void notifySetup()
            {
                //creating a new thread for the tray icon, so it works everywhere
                Thread notifyThread = new Thread(
                delegate()
                {
                    trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    trayIcon.ContextMenu = new ContextMenu(new MenuItem[] { new MenuItem("Exit", Exit) });
                    trayIcon.Visible = true;
                    Application.Run();
                });
                notifyThread.Start();
            }
            public MyCustomApplicationContext()
            {
                notifySetup();
                //just a check in case the listner is active
                if (globalControl.listnerControl == true)
                {
                    GlobalListener.listener.StartListening();
                    globalControl.listnerControl = false;
                }
                listner();

            }

            public void Exit(object sender, EventArgs e)
            {
                // Hide tray icon, otherwise it will remain shown until user mouses over it
                GlobalListener.listener.StartListening();
                trayIcon.Visible = false;
                Environment.Exit(-1);
            }
        }

        static void Main(string[] args)
        {
            //calling the real "main" (MyCustomApplicationContext)
            Application.Run(new MyCustomApplicationContext());
        }

    }
}
