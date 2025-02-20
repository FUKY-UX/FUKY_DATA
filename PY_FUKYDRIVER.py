import tkinter as tk
from tkinter import ttk, messagebox
import asyncio
import sys
from bleak import BleakScanner
import threading
import queue

# 定义 BLE 服务和特征的 UUID
SERVICE_UUID = "00001800-0000-1000-8000-00805f9b34fb"
CHARACTERISTIC_UUID = "0x2A00"
EXPECTED_NAME = "FUKY_MOUSE"


# 设置Windows系统的事件循环策略
if sys.platform == 'win32':
    asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

class BLEScannerApp:
    def __init__(self, master):
        self.master = master
        master.title("FUKYDATA")
        master.geometry("500x400")

        # 创建界面组件
        self.scan_button = ttk.Button(master, text="开始搜索", command=self.start_scan)
        self.scan_button.pack(pady=10)

        self.tree = ttk.Treeview(master, columns=("Name", "Address"), show="headings")
        self.tree.heading("Name", text="设备名称")
        self.tree.heading("Address", text="MAC地址")
        self.tree.column("Name", width=200)
        self.tree.column("Address", width=250)
        self.tree.pack(fill=tk.BOTH, expand=True, padx=10, pady=5)

        # 初始化变量
        self.is_scanning = False
        self.devices = {}
        self.queue = queue.Queue()
        self.update_interval = 200  # 毫秒

        # 启动队列处理
        self.master.after(self.update_interval, self.process_queue)

    def start_scan(self):
        if self.is_scanning:
            return
            
        self.is_scanning = True
        self.scan_button.config(text="扫描中...")
        self.devices.clear()
        self.tree.delete(*self.tree.get_children())

        # 启动扫描线程
        scan_thread = threading.Thread(target=self.run_scan, daemon=True)
        scan_thread.start()

    def run_scan(self):
        async def _scan():
            try:
                scanner = BleakScanner(detection_callback=self.detection_callback)
                await scanner.start()
                # 持续扫描直到程序退出
                while True:
                    await asyncio.sleep(1)
            except Exception as e:
                self.queue.put(("error", str(e)))
            finally:
                await scanner.stop()
                self.queue.put(("status", False))

        asyncio.run(_scan())

    def detection_callback(self, device, advertisement_data):
        name = device.name or "Unknown"
        self.queue.put(("device", (device.address, name)))

    def process_queue(self):
        while not self.queue.empty():
            item_type, data = self.queue.get()
            if item_type == "device":
                address, name = data
                if address not in self.devices:
                    self.devices[address] = name
                    self.tree.insert("", tk.END, values=(name, address))
            elif item_type == "error":
                messagebox.showerror("错误", f"扫描时发生错误:\n{data}")
                self.is_scanning = False
                self.scan_button.config(text="开始搜索")
            elif item_type == "status":
                self.is_scanning = data
                self.scan_button.config(text="开始搜索")

        self.master.after(self.update_interval, self.process_queue)

if __name__ == "__main__":
    root = tk.Tk()
    app = BLEScannerApp(root)
    root.mainloop()