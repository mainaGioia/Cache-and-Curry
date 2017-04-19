using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Finetuner.Services.Storage.Utils
{
    class ObservableQueue<T> : Queue<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {

        public ObservableQueue() : base() { }

        public ObservableQueue(List<T> list) : base((list != null)? new List<T>(list.Count) : list) { }

       
        public new virtual void Enqueue(T item)
        {
            base.Enqueue(item);
            OnCollectionChanged(NotifyCollectionChangedAction.Add, item);
        }
       
        //------------------------------------------------------
        #region INotifyPropertyChanged implementation
        /// <summary>
        /// PropertyChanged event
        /// </summary>
        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add
            {
                PropertyChanged += value;
            }
            remove
            {
                PropertyChanged -= value;
            }
        }
        #endregion INotifyPropertyChanged implementation

        protected virtual event PropertyChangedEventHandler PropertyChanged;


        //
        // Summary:
        //     Occurs when an item is added, removed, changed, moved, or the entire list is
        //     refreshed.
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        private void OnCollectionChanged(NotifyCollectionChangedAction action, object item)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, item));
        }


        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (CollectionChanged != null)
            {
                using (BlockReentrancy())
                {
                    CollectionChanged(this, e);
                }
            }
        }


        protected IDisposable BlockReentrancy()
        {
            _monitor.Enter();
            return _monitor;
        }
        private SimpleMonitor _monitor = new SimpleMonitor();



        private class SimpleMonitor : IDisposable
        {
            public void Enter()
            {
                ++_busyCount;
            }

            public void Dispose()
            {
                --_busyCount;
            }

            public bool Busy { get { return _busyCount > 0; } }

            int _busyCount;
        }

    }


}
