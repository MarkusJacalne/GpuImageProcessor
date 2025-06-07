# GpuImageProcessor

# C# OpenCL Image Processor

A Windows Forms application built in C# to demonstrate and compare the performance of CPU vs. GPU image processing using the OpenCL framework. This project was created to explore GPGPU concepts and low-level hardware interaction from a managed .NET environment.

## Key Features

* **CPU vs. GPU Comparison:** Process images using both a standard C# loop and a custom OpenCL kernel.
* **Performance Scoring:** A scoring system quantifies the speedup factor of the GPU over the CPU.
* **Dynamic UI:** The UI is created programmatically without the WinForms designer.
* **Hardware Diagnostics:** Includes a diagnostic tool to detect and report all available OpenCL platforms and devices on the system.

## How to Run

1.  **Prerequisites:**
    * Visual Studio 2022 with the .NET Framework workload.
    * An OpenCL-capable GPU with up-to-date drivers.
    * The `Silk.NET.OpenCL` NuGet package installed in the project.
2.  **Configuration:**
    * In Visual Studio, right-click the project -> **Properties**.
    * Go to the **Build** tab and check **"Allow unsafe code"**.
3.  **Build and Run.**

## What I Learned

This was my first project exploring GPGPU programming. Key takeaways include:

* **C# Interoperability:** Using `unsafe` code, `fixed` statements, and `stackalloc` to interface with low-level C-style APIs like OpenCL.
* **GPU Computing Concepts:** Understanding the overhead of data transfer (RAM to VRAM) vs. the immense speed of parallel computation.
* **Driver & Environment Debugging:** Troubleshooting system-level issues like switchable graphics on laptops and ensuring the correct OpenCL runtime is accessible.
* **Visual Studio Configuration:** Managing project files and their properties, such as setting "Copy to Output Directory" for the `.cl` kernel file.
