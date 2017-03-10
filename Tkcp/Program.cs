using System.Diagnostics;
using System;
using System.Text;
using System.Threading;
namespace Tkcp
{
    class Program
    {


        static void Main(string[] args)
        {
            

            try
            {
                var client = new TkcpClient();
                bool startSend = false;
                client.ConnectEvent += (ConnectResult result) => 
                {
                    if (result == ConnectResult.kSucceed)
                    {
                        startSend = true;
                        Debug.Assert(client.Connected());

                    }
                    else
                    {
                        Console.WriteLine("connect failed");
                        Debug.Assert(client.Disconnected());
                    }

                };

                client.MsgEvent += (byte[] msg, int offset, int count) =>
                {
                    Console.WriteLine(Encoding.UTF8.GetString(msg, offset, count));
                };
                client.Connect("192.168.1.17", 5000);
                int num = 10;
                for (;;)
                {
                    if (startSend)
                    {
                        client.Send(Encoding.UTF8.GetBytes("hello world!"));
                        num--;
                        if (num < 0)
                        {
                            client.Close();
                            break;
                        }
                    }
                    Thread.Sleep(2 * 1000);
                }

                Thread.Sleep(10 * 1000);
                Debug.Assert(client.Disconnected());
 
            }
            catch (System.Exception e)
            {

                Console.WriteLine(e.ToString());
            }
            
        }
    }
}
