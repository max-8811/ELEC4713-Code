# On-Device AI Sports Coach (Kinect + Local LLM)

This repository contains the source code for a novel, privacy-preserving automated sports coach. The project was developed as part of the "Human Posture Recognition in Sports Training" thesis and combines a Microsoft Kinect v2 for 3D skeletal tracking with a locally-run Large Language Model (LLM) to provide real-time biomechanical feedback.

The system is 100% on-device (serverless) to ensure user privacy.


# System Dependencies

Hardware

- Microsoft Kinect v2 Sensor (with the necessary PC adapter).

- An Ollama supported GPU (optional)

Software

- Windows 7/10 Operation System

- Visual Studio 2013 (or newer, project is built on .NET 3.5).

- Kinect for Windows SDK 2.0

- Ollama (for running the local LLM).


# How to Run

### Install Dependencies

* Kinect SDK: Download and install the Kinect for Windows SDK 2.0. 
https://www.microsoft.com/en-us/download/details.aspx?id=44561

* Ollama: Download and install Ollama from ollama.com. Ensure the Ollama service is running in the background. 
https://ollama.com/

### Set Up the Ollama LLM

This application communicates with a local LLM through Ollama. You must first pull the base model and then create the custom "coach" models that the application uses.

Pull the Base Model:
Open your terminal (PowerShell or Command Prompt) and run the following command to download the ```llama3.2:3b model```, which was identified as optimal in the thesis. Or any other model specified in the ```Modelfile``` folder.

```ollama pull llama3.2:3b-instruct-q4_K_M```


(This is the base model specified in ```Modelfilellama3.2(3bkm).json```)

### Create the Custom Coach Models:

The application uses custom Modelfile definitions (like the one you provided) to set the LLM's system prompt, parameters, and personality. You must create these models in Ollama.

* Navigate to the ```/Modelfiles``` directory of this project in your terminal.

* Run the ```ollama create``` command for each coach personality. For example, to create the default coach model based on ```Modelfilellama3.2(3bkm).json```, you might name it ```default-coach```:

```ollama create default-coach -f ./Modelfilellama3.2(3bkm).json```


IMPORTANT: The application code (```OllamaClient.cs```) selects models based on the UI. The thesis mentions models like ```friendly3bmcoach``` and ```strict3bmcoach```. You must create a model for each name the application will call.

For example, if you have ```Modelfile-friendly.json```, you would run:

```ollama create friendly3bmcoach -f ./ModFile-friendly.json```


### Run the Application

1. Make sure your Kinect v2 is plugged in and recognized by your PC.

2. Ensure the Ollama application is running in the background.

3. Open the WpfApplication1.sln file in Visual Studio.

4. Press Start (F5) to build and run the project.

5. From the application's menu, select the exercise and any coach (e.g., "friendly", "strict") that you have created in Ollama.