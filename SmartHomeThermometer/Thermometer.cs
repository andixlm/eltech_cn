using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmartHomeThermometer
{
    class Thermometer : IDisposable
    {
        // Температура по умолчанию.
        public static readonly double DEFAULT_TEMPERATURE = 0.0;
        // Значение изменения температуры.
        public static readonly double DELTA_TEMPERATURE = 1.0;
        // Период обновления по умолчанию.
        public static readonly int DEFAULT_UPDATE_INTERVAL = 1;
        // Генератор случайных чисел.
        private static readonly Random sRandom = new Random();
        // callback, вызываемый при обновлении температуры.
        public delegate void OnTemperatureUpdateFunc(double temperature);
        // Мьютекс для синхронизации обновления данных.
        private Mutex _Mutex;
        // Поток, в котором происходит генерация температуры.
        private Thread _WorkerThread;
        // Температура.
        private double _Temperature;
        public double Temperature
        {
            get
            {
                return _Temperature;
            }
        }

        // Период обновления.
        private int _UpdateInterval;
        public int UpdateInterval
        {
            get
            {
                _Mutex.WaitOne();
                int interval = _UpdateInterval;
                _Mutex.ReleaseMutex();

                return interval / 1000;
            }

            set
            {
                if (value < 1 || value > 10)
                    throw new Exception("Incorrect UpdateInterval value");

                _Mutex.WaitOne();
                _UpdateInterval = 1000 * value;
                _Mutex.ReleaseMutex();
            }
        }
        // Время последнего обновления.
        private DateTime _LastUpdateTime;
        public DateTime LastUpdateTime
        {
            get
            {
                return _LastUpdateTime;
            }
        }
        // callback, вызываемый при обновлении температуры.
        private OnTemperatureUpdateFunc _OnTemperatureUpdated;
        public OnTemperatureUpdateFunc OnTemperatureUpdate
        {
            set
            {
                _OnTemperatureUpdated = value;
            }
        }

        public Thermometer()
        {
            _Temperature = DEFAULT_TEMPERATURE;
            _UpdateInterval = 1000 * DEFAULT_UPDATE_INTERVAL;
            _LastUpdateTime = DateTime.Now;

            _Mutex = new Mutex();
            _WorkerThread = new Thread(new ThreadStart(Run));
            _WorkerThread.Start();
        }

        ~Thermometer()
        {
            Dispose();
        }

        // Генерация температуры.
        private void Run()
        {
            while (true)
            {
                UpdateTemperature();

                _Mutex.WaitOne();
                int interval = _UpdateInterval;
                _Mutex.ReleaseMutex();

                Thread.Sleep(interval);
            }
        }
        // Обновление температуры.
        public void UpdateTemperature()
        {
            _Mutex.WaitOne();
            // Прибавляется случайное значение от -1.0 до 1.0.
            _Temperature += (sRandom.NextDouble() < 0.5 ? -1.0 : 1.0) * sRandom.NextDouble() * DELTA_TEMPERATURE;
            _LastUpdateTime = DateTime.Now;

            _Mutex.ReleaseMutex();

            _OnTemperatureUpdated?.Invoke(_Temperature);
        }

        public void Dispose()
        {
            _WorkerThread.Abort();
        }
    }
}
