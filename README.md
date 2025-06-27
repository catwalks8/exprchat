# Expressive Chat
A simple LLM chat client for koboldcpp with dynamic character portraits.

## Description
A slight upgrade over small static chat icons. Instead, just one text bubble for the latest response, and a big portrait image showing a character's expression to match it. The idea is making it feel more personal and dynamic, I guess.

Given a collection of images featuring the same character showing different emotions, the app will ask an LLM to pick one fitting the latest reply, and display it shortly after.

## Requirements
- **KoboldCpp** - Used as AI backend
- **GGUF LLM** - The language model that can be run locally through koboldcpp
- **Portrait images** - Multiple image files saved in one folder to be used as character portraits
- *(optional)* **AI Horde account** - Not necessary for use as an API Provider, but recommended for shorter queue times.