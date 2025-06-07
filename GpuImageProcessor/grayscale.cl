/*
  This is an OpenCL Kernel. It's a small program that runs on the GPU for each pixel.
  - __kernel:         Declares this as a function the GPU can run.
  - __global uchar4*: Declares a pointer to an array of 4-part unsigned characters (Red, Green, Blue, Alpha)
                      in the GPU's global memory.
  - get_global_id(0): Gets the unique ID of the current pixel being processed.
*/
__kernel void ToGrayscale(__global const uchar4* input, __global uchar4* output)
{
    // Get the ID of the current pixel we are working on.
    int id = get_global_id(0);

    // Get the actual pixel data (RGBA values) from the input image.
    uchar4 pixel = input[id];

    // Calculate the grayscale value (luminance) using the standard formula.
    // We use floating point numbers for accuracy.
    // pixel.x = Red, pixel.y = Green, pixel.z = Blue
    float luminance = 0.2126f * pixel.x + 0.7152f * pixel.y + 0.0722f * pixel.z;

    // Convert the float result back to a character (0-255).
    uchar gray = (uchar)luminance;

    // Write the new grayscale pixel to the output image.
    // We set Red, Green, and Blue to the same 'gray' value.
    // pixel.w is the Alpha (transparency), which we leave unchanged.
    output[id] = (uchar4)(gray, gray, gray, pixel.w);
}