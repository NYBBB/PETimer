using System;
using System.Collections.Generic;
using System.Threading;
using System.Timers;

namespace ConsoleTimer
{
    class Program
    {
        private static readonly string obj = "lock";
        static void Main(string[] args)
        {
            Console.WriteLine("Test Start");
            //TimerTest();
            test2();
            Console.ReadKey();
        }
        //在主线程检测并处理
        static void Test1()
        {
            PETimer pt = new PETimer();
            pt.SetLog((string Info) =>
            {
                Console.WriteLine("ConsoleLog : " + Info);
            });

            pt.AddTimeTask((int tid) =>
            {
                Console.WriteLine("Time : " + DateTime.Now);
                //打印线程ID
                Console.WriteLine("Process线程ID : ", Thread.CurrentThread.ManagedThreadId.ToString());
            }, 1000, PETimeUnit.Millisecond, 0);

            while (true)
            {
                pt.Update();
            }
        }

        //独立的线程检测并处理任务
        static void test2()
        {
            Queue<TaskPack> tpQue = new Queue<TaskPack>();
            PETimer pt = new PETimer(50);//传入循环间隔
            pt.AddTimeTask((int tid) =>
            {
                Console.WriteLine(tid);
                //Console.WriteLine("Time : " + DateTime.Now);
                //打印线程ID
                Console.WriteLine("Process线程ID : " + Thread.CurrentThread.ManagedThreadId.ToString());
            }, 1000, PETimeUnit.Millisecond, 0);

            pt.SetHandle((Action<int> cb, int tid) =>
            {
                if (cb != null)
                {
                    lock (obj)
                    {
                        tpQue.Enqueue(new TaskPack(tid, cb));
                    }
                }
            });

            while (true)
            {
                if (tpQue.Count > 0)
                {
                    TaskPack tp;
                    lock (obj)
                    {
                        tp = tpQue.Dequeue();
                    }
                    tp.cb(tp.tid);
                }
            }
        }

        static void TimerTest()
        {
            //开启一个线程并每个（）毫秒进行一次循环
            System.Timers.Timer t = new System.Timers.Timer(/**/);
            t.AutoReset = true;
            t.Elapsed += OnTimedEvent;
            t.Start();
        }
        private static void OnTimedEvent(object soutce, System.Timers.ElapsedEventArgs e)
        {
            Console.WriteLine("Time : " + DateTime.Now);
            //打印线程ID
            Console.WriteLine("Process线程ID : ", Thread.CurrentThread.ManagedThreadId.ToString());
        }
    }
}

class TaskPack
{
    public int tid;
    public Action<int> cb;
    public TaskPack(int tid, Action<int> cb)
    {
        this.tid = tid;
        this.cb = cb;
    }
}