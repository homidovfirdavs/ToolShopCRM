import axios from "axios";

const apiClient = axios.create({
  baseURL: "http://localhost:5252/api",
  headers: {
    "Content-Type": "application/json",
  },
});

export default apiClient;