/********************************************************************
	created:	2020/12/23
	created:	23:12:2020   16:56
	file base:	PETimer
	file ext:	cs
	author:		NYB
	
	purpose:	计时器

开发目标：
支持时间定时、帧定时
定时任务可循环、可取消、可替换
使用简单、调用方便

///*************开发细节*************//****************注意：：：：：：现在这个定时系统是一个和Unity没有半毛钱关系的系统，不依赖Unity
计时单位：毫秒，秒，分钟，小时，天
运算时将所有单位统一换算为毫秒

执行次数ExecutionTimes：等于0的话代表一直循环，其他的代表循环次数
//取消任务：生产一个全局唯一的ID，通过ID索引操作任务
//任务替换：使用ID，

//将核心细节脱离了Unity的控制，也就是说可以在服务器和客户端都可以使用这个类
//Update-和日志输出的调用在别的地方初始化这类时进行设置。Update-在别地的Update中调用，日志输出设置调用SetLog(Action<string> log)函数。
//比如：        //设置日志工具
        PETimer.SetLog((string info) =>
        {
            Debug.Log("PETimerLog : " + info);
        });
//在客户端上将Log输出设置为Debug.Log();
//时间脱离Unity：
    获得当前的时间减去计算机元年时间（1970，1，1，0，0，0）
    再将这时间差计算成毫秒，就可以代替Unity的时间了
//
//将定时任务检测放在多线程里面，但函数回调在主线程：
    任务的达成条件后不是直接处理，把满足条件的任务放到taskHandle中处理，在调用的时候给TaskHandle设置一个函数，使用这个函数去处理
    我在这项目中处理使用的是将要处理的函数添加到一个Queue（队列）中，队列里面存储所有要执行的任务，
    最后再通过在主线程中的while循环对Queue读取，执行回调函数
*********************************************************************/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Timers;

public class PETimer
{
    private Action<string> taskLog;
    //
    private Action<Action<int>, int> TaskHandle;
    //锁
    private static readonly string obj = "lock";
    private static readonly string LockTime = "lockTime";
    //声名日期
    private DateTime startDetaTime = new DateTime(1970, 1, 1, 0, 0, 0);
    //现在时间
    private double NowTime;
    //
    private System.Timers.Timer srvTimer;
    //全局ID
    private int TaskID;
    //保存全局ID
    private List<int> TidList = new List<int>();
    //回收空ID
    private List<int> RecTidList = new List<int>();

    //实例化辅助运算类
    TimerSystemAuxilaryFunction TimerAuxilary = new TimerSystemAuxilaryFunction();

    //定时任务缓存列表
    private List<PETimeTask> TempTimeList = new List<PETimeTask>();
    //定时任务列表
    private List<PETimeTask> taskTimeList = new List<PETimeTask>();

    public int FrameCounter;//当前帧
    //定时帧任务缓存列表
    private List<PEFrameTask> TempFrameList = new List<PEFrameTask>();
    //定时帧任务列表
    private List<PEFrameTask> taskFrameList = new List<PEFrameTask>();
    //实例化时传递多少毫秒调用一次
    public PETimer(int interval = 0)
    {
        TidList.Clear();
        RecTidList.Clear();
        TempFrameList.Clear();
        TempTimeList.Clear();
        taskFrameList.Clear();
        taskTimeList.Clear();

        if (interval != 0)
        {
            //设置循环间隔
            srvTimer = new System.Timers.Timer(interval);
            srvTimer.AutoReset = true;

            //添加委托
            srvTimer.Elapsed += (object soutce, System.Timers.ElapsedEventArgs e) =>
            {
                Update();
            };
            srvTimer.Start();
        }
    }
    public void Update()
    {
        FrameCounter++;
        //检测定时和帧定时任务
        CheckframeTask();
        CheckTimeTask();
        //回收（移除）ID
        RecycleTaskID();

    }
    //时间定时检测
    private void CheckTimeTask()
    {
        if (TempTimeList.Count > 0)
        {
            lock (LockTime)
            {
                //加载缓冲列表内的任务
                for (int Index = 0; Index < TempTimeList.Count; Index++)
                {
                    taskTimeList.Add(TempTimeList[Index]);
                }
                //清口缓存列表
                TempTimeList.Clear();
            }
        }
        
        //遍历定时列表检测任务是否达到条件
        for (int Index = 0; Index < taskTimeList.Count; Index++)
        {
            NowTime = GetUTCMilliseconds();
            PETimeTask task = taskTimeList[Index];
            if (task.GoldDelayTime <= NowTime)
            {
                Action<int> cb = task.Callback;
                try
                {
                    if (TaskHandle != null)
                    {
                        TaskHandle(cb, task.TaskID);
                    }
                    else
                    {
                        if (cb != null)
                        {
                            cb(task.TaskID);//调用函数
                        }
                    }
                }
                catch (Exception e)
                {
                    LogInfo(e.ToString());
                }
                //处理循环次数
                if (task.ExecutionTimes == 1)
                {
                    //移除已经完成的任务
                    taskTimeList.RemoveAt(Index);
                    //将索引减一，防止跳过某个任务
                    Index--;
                    //将ID添加到要移除的列表力
                    RecTidList.Add(task.TaskID);
                }
                else
                {
                    if (task.ExecutionTimes != 0)
                    {
                        task.ExecutionTimes--;
                    }
                    NowTime = GetUTCMilliseconds();
                    task.GoldDelayTime = NowTime + task.Delay;
                }
            }
        }

    }
    //帧定时检测                           
    private void CheckframeTask()
    {
        //加载缓冲列表内的任务
        for (int Index = 0; Index < TempFrameList.Count; Index++)
        {
            taskFrameList.Add(TempFrameList[Index]);
        }
        //清口缓存列表
        TempFrameList.Clear();
        //遍历定时列表检测任务是否达到条件
        for (int Index = 0; Index < taskFrameList.Count; Index++)
        {
            PEFrameTask task = taskFrameList[Index];
            if (task.DestFrame <= FrameCounter)
            {
                Action<int> cb = task.Callback;
                try
                {
                    if (TaskHandle != null)
                    {
                        TaskHandle(cb, task.TaskID);
                    }
                    else
                    {
                        if (cb != null)
                        {
                            cb(task.TaskID);//调用函数
                        }
                    }
                }
                catch (Exception e)
                {
                    LogInfo(e.ToString());
                }
                //处理循环次数
                if (task.ExecutionTimes == 1)
                {
                    //移除已经完成的任务
                    taskFrameList.RemoveAt(Index);
                    //将索引减一，防止跳过某个任务
                    Index--;
                    //将ID添加到要移除的列表力
                    RecTidList.Add(task.TaskID);
                }
                else
                {
                    if (task.ExecutionTimes != 0)
                    {
                        task.ExecutionTimes--;
                    }
                    task.DestFrame = FrameCounter + task.Delay;
                }
            }
        }

    }
    //重置系统
    public void Reset()
    {
        TaskID = 0;
        TidList.Clear();
        RecTidList.Clear();
        TempFrameList.Clear();
        TempTimeList.Clear();
        taskFrameList.Clear();
        taskTimeList.Clear();

        taskLog = null;
        srvTimer.Stop();
    }
    //设置回调的函数
    public void SetHandle(Action<Action<int>, int> handle)
    {
        TaskHandle = handle;
    }

    //获取当前的时间
    public double GetMillisencondsTime()
    {
        return NowTime;
    }
    public DateTime GetLocalDateTime()
    {
        DateTime dt = TimeZone.CurrentTimeZone.ToLocalTime(startDetaTime.AddMilliseconds(NowTime));
        return dt;
    }
    public int GetYear()
    {
        return GetLocalDateTime().Year;
    }
    public int GetMonth()
    {
        return GetLocalDateTime().Month;
    }
    public int GetDay()
    {
        return GetLocalDateTime().Day;
    }
    public int GetWeek()
    {
        return (int)GetLocalDateTime().DayOfWeek;
    }
    public string GetLocalTimeString()
    {
        DateTime dt = GetLocalDateTime();
        string str = GetTimeStr(dt.Hour) + ":" + GetTimeStr(dt.Minute) + ":" + GetTimeStr(dt.Second);
        return str;
    }

    #region TimeDask
    //添加定时任务（调用函数，延迟时间，时间单位，执行次数）
    public int AddTimeTask(Action<int> Callback, double delay, PETimeUnit TimeUnit = PETimeUnit.Millisecond, int Count = 1)
    {
        //获得ID
        int TempID = GetTaskID();
        //添加到缓存列表
        NowTime = GetUTCMilliseconds();
        lock (LockTime)
        {
            TempTimeList.Add(new PETimeTask(TempID, Callback, TimerAuxilary.ChangeTimeToSecond(delay, TimeUnit),
                (NowTime + TimerAuxilary.ChangeTimeToSecond(delay, TimeUnit)), Count));
        }
        TidList.Add(TempID);
        return TempID;
    }
    //删除定时任务
    public bool DeleteTimeTask(int TempID)
    {
        bool bExist = false;
        for (int i = 0; i < taskTimeList.Count; i++)
        {
            PETimeTask task = taskTimeList[i];
            if (task.TaskID == TempID)
            {
                taskTimeList.RemoveAt(i);
                for (int j = 0; j < TidList.Count; j++)
                {
                    TidList.RemoveAt(j);
                }
                bExist = true;
                break;
            }
        }
        if (!bExist)
        {
            for (int i = 0; i < TempTimeList.Count; i++)
            {
                if (TempTimeList[i].TaskID == TempID)
                {
                    bExist = true;
                    TempTimeList.RemoveAt(i);
                    break;
                }
            }
        }
        return bExist;
    }
    //替换任务（要替换的任务ID，调用函数，延迟时间，时间单位，执行次数）
    public bool ReplaceTimeTask(int TaskID, Action<int> Callback, float delay, PETimeUnit TimeUnit = PETimeUnit.Millisecond, int Count = 1)
    {
        bool bTemp = false;
        NowTime = GetUTCMilliseconds();
        PETimeTask NewTask = new PETimeTask(TaskID, Callback, TimerAuxilary.ChangeTimeToSecond(delay, TimeUnit),
            (NowTime + TimerAuxilary.ChangeTimeToSecond(delay, TimeUnit)), Count);
        for (int i = 0; i < taskTimeList.Count; i++)
        {
            if (taskTimeList[i].TaskID == TaskID)
            {
                //传入ID的任务替换成新的任务
                taskTimeList[i] = NewTask;
                bTemp = true;
                break;
            }
        }
        //如果任务中没有
        if (!bTemp)
        {
            //遍历缓存任务
            for (int i = 0; i < TempTimeList.Count; i++)
            {
                if (TempTimeList[i].TaskID == TaskID)
                {
                    //传入ID的任务替换成新的任务
                    TempTimeList[i] = NewTask;
                    bTemp = true;
                    break;
                }
            }
        }
        return bTemp;
    }
    #endregion

    #region FramdDask
    //添加帧定时任务（调用函数，延迟时间，时间单位，执行次数）
    public int AddFrameTask(Action<int> Callback, int delay, int Count = 1)
    {
        //获得ID
        int TempID = GetTaskID();
        //添加到帧缓存列表
        TempFrameList.Add(new PEFrameTask(TempID, Callback, delay, FrameCounter + delay, Count));
        TidList.Add(TempID);
        return TempID;
    }
    //删除帧定时任务
    public bool DeleteFrameTask(int TempID)
    {
        bool bExist = false;
        for (int i = 0; i < taskFrameList.Count; i++)
        {
            PEFrameTask task = taskFrameList[i];
            if (task.TaskID == TempID)
            {
                taskFrameList.RemoveAt(i);
                //移除
                for (int j = 0; j < TidList.Count; j++)
                {
                    TidList.RemoveAt(j);
                }
                bExist = true;
                break;
            }
        }
        if (!bExist)
        {
            for (int i = 0; i < TempFrameList.Count; i++)
            {
                if (TempFrameList[i].TaskID == TempID)
                {
                    bExist = true;
                    TempFrameList.RemoveAt(i);
                    break;
                }
            }
        }
        return bExist;
    }
    //替换帧定时任务（要替换的任务ID，调用函数，延迟时间，时间单位，执行次数）
    public bool ReplaceFrameTask(int TaskID, Action<int> Callback, int delay, int Count = 1)
    {
        bool bTemp = false;
        PEFrameTask NewTask = new PEFrameTask(TaskID, Callback, delay, FrameCounter + delay, Count);
        for (int i = 0; i < taskFrameList.Count; i++)
        {
            if (taskFrameList[i].TaskID == TaskID)
            {
                //传入ID的任务替换成新的任务
                taskFrameList[i] = NewTask;
                bTemp = true;
                break;
            }
        }
        //如果任务中没有
        if (!bTemp)
        {
            //遍历缓存任务
            for (int i = 0; i < taskFrameList.Count; i++)
            {
                if (taskFrameList[i].TaskID == TaskID)
                {
                    //传入ID的任务替换成新的任务
                    taskFrameList[i] = NewTask;
                    bTemp = true;
                    break;
                }
            }
        }
        return bTemp;
    }
    #endregion

    #region Tool Methonds
    //生成一个全局ID
    private int GetTaskID()
    {
        lock (obj)
        {
            TaskID += 1;

            //安全代码，以防万一TaskID值超出最大值
            while (true)
            {
                if (TaskID >= int.MaxValue)
                {
                    TaskID = 0;
                }
                //是否被使用
                bool used = false;
                //遍历所有检测有没有相同的
                for (int i = 0; i < TidList.Count; i++)
                {
                    if (TaskID == TidList[i])
                    {
                        used = true;
                        break;
                    }
                }
                //没有被使用过
                if (!used)
                {
                    break;
                }
                else
                {
                    TaskID += 1;
                }
            }
        }
        return TaskID;
    }
    //回收ID
    private void RecycleTaskID()
    {
        if (RecTidList.Count > 0)
        {
            for (int i = 0; i < RecTidList.Count; i++)
            {
                int TempID = RecTidList[i];
                for (int j = 0; j < TidList.Count; j++)
                {
                    if (TidList[j] == TempID)
                    {
                        TidList.RemoveAt(j);
                        break;
                    }
                }
            }
            //清空回收缓存列表
            RecTidList.Clear();
        }
    }
    //设置日志输出工具
    public void SetLog(Action<string> log)
    {
        taskLog = log;
    }
    //日志输出
    private void LogInfo(string Info)
    {
        if (taskLog != null)
        {
            taskLog(Info);
        }
    }
    //将获得当前时间标准时间（毫秒）
    private double GetUTCMilliseconds()
    {
        TimeSpan ts = DateTime.UtcNow - startDetaTime;//现在的标准时间减去起始时间
        return ts.TotalMilliseconds;
    }
    //
    private string GetTimeStr(int time)
    {
        if (time < 10)
        {
            return "0" + time;
        }
        else
        {
            return time.ToString();
        }
    }
    #endregion
}

//定时系统辅助运算类
public class TimerSystemAuxilaryFunction
{

    //换算时间f
    public double ChangeTimeToSecond(double Time, PETimeUnit timeUnit)
    {
        double TempTime = 0;

        switch (timeUnit)
        {
            case PETimeUnit.Millisecond:
                TempTime = Time;
                break;
            case PETimeUnit.Second:
                TempTime = Time * 1000;
                break;
            case PETimeUnit.Minute:
                TempTime = Time * 1000 * 60;
                break;
            case PETimeUnit.Hour:
                TempTime = Time * 1000 * 60 * 60;
                break;
            case PETimeUnit.Day:
                TempTime = Time * 1000 * 24 * 60 * 60;
                break;
        }
        return TempTime;
    }
}

/********************************************************************
	created:	2020/12/20
	created:	20:12:2020   14:41
	file base:	PETimeTask
	file ext:	cs
	author:		NYB
	purpose:	定时任务数据类
*********************************************************************/
public class PETimeTask
{
    //任务的ID
    public int TaskID;
    //延迟的时间
    public double Delay;
    //延迟到的目标时间
    public double GoldDelayTime;
    //执行的函数
    public Action<int> Callback;
    //要执行次数
    public int ExecutionTimes;

    public PETimeTask(int taskID, Action<int> Callback_, double Delay_, double GoldDelayTime_, int Count)
    {
        TaskID = taskID;
        Callback = Callback_;
        Delay = Delay_;
        GoldDelayTime = GoldDelayTime_;
        ExecutionTimes = Count;
    }
}
public class PEFrameTask
{
    //任务的ID
    public int TaskID;
    //延迟的帧数
    public int Delay;
    //目标帧
    public int DestFrame;
    //执行的函数
    public Action<int> Callback;
    //要执行次数
    public int ExecutionTimes;

    public PEFrameTask(int taskID, Action<int> Callback_, int Delay_, int DestFrame_, int Count)
    {
        TaskID = taskID;
        Callback = Callback_;
        Delay = Delay_;
        DestFrame = DestFrame_;
        ExecutionTimes = Count;
    }
}
//延迟时间单位
public enum PETimeUnit
{
    //毫秒
    Millisecond,
    //秒
    Second,
    //分钟
    Minute,
    //小时
    Hour,
    //天
    Day
}